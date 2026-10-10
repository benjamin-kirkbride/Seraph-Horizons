"""The classifier generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks
its rule. Everything is in the build frame (voxels), on the model before the origin shift;
`check_shipped` in make_shape.py proves the shipped files are this model moved.

A pose here is (theta, state): the axle's angle and the tier state whose parts are fitted (`STATES` in
make_shape.py). Every tier's parts are in the one shape, and tier 2's and tier 3's stand in the same
space, so every check that compares parts with each other runs per state.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
from machinegen.checks import supports as shaft_supports
from machinegen.geometry import aabb_of, rotate
from machinegen.rigmath import apply as _apply
from machinegen.rigmath import part_of, posed


class V:
    def __init__(self, m, els, parts, rig):
        self.m, self.els, self.parts, self.rig = m, els, parts, rig
        self.by_id = {p["id"]: p for p in parts}
        self.by_part = {}
        for el in els:
            self.by_part.setdefault(el.part, []).append(el)
        self.cache = {}
        self.ok = True

    def fail(self, msg):
        self.ok = False
        print("FAIL", msg)

    def fitted(self, pid, state):
        return self.m.part_fitted(self.by_id, pid, state)

    def mat(self, pid, theta):
        return self.m.pm(self.parts, pid, (theta, None))

    def posed(self, pid, theta):
        key = (pid, round(theta, 9))
        if key not in self.cache:
            if len(self.cache) > 20000:
                self.cache.clear()
            mm = self.mat(pid, theta)
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def state_els(self, pose):
        """Every element fitted in the pose's state, posed: (part, element) pairs."""
        theta, state = pose
        return [(pid, e) for pid in self.by_part if self.fitted(pid, state) for e in self.posed(pid, theta)]

    def point(self, pid, theta, p):
        q = _apply(self.mat(pid, theta), [c / 16 for c in p])
        return [c * 16 for c in q]

    def named(self, pid, rx, theta=None):
        src = self.posed(pid, theta) if theta is not None else self.by_part.get(pid, [])
        r = re.compile(rx)
        return [e for e in src if r.search(e.name)]


STATE_NAMES = ("frame", "tier2", "tier3", "tier4")


def thetas(n, turns=3):
    """n angles over the drum's cycle (three axle turns), off the round numbers."""
    return [round(math.tau * turns * (i + 0.37) / n, 6) for i in range(n)]


# ---------------------------------------------------------------- structure
def check_basic(v):
    worst = euler_round_trip(v.els)
    print(f"euler round trip: worst {worst:.1e}")
    if worst > 1e-9:
        v.fail("an element's rotation does not survive being written as Euler angles")
    names = [el.name for el in v.els]
    seen, dup = set(), set()
    for n in names:
        (dup if n in seen else seen).add(n)
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
    # the requires vocabulary: every value in a tier, each tier's set fitted only by it and the next where shared
    used = {p["requires"] for p in v.parts} - {None}
    tiered = set().union(*v.m.STATES.values())
    if used != tiered:
        v.fail(f"requires values {sorted(used ^ tiered)} are not exactly the tiers' sets")
    print("requires: " + "; ".join(f"{k} {sorted(s)}" for k, s in v.m.STATES.items()))


def check_floating(v):
    """Nothing floats: the frame is one piece from the ground, and in every tier state the frame and that
    tier's parts at rest are too (each fitted part is held by what is already there)."""
    seen, loose = frame_floating(v.by_part["frame"])
    print(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")
    for state in STATE_NAMES[1:]:
        els = [e for _, e in v.state_els((0.0, state))]
        _, loose = frame_floating(els)
        print(f"build {state}: {len(els)} elements, {len(loose)} not joined to the ground")
        if loose:
            v.fail(f"{state}: elements float: {loose[:8]}")


def check_containment(v):
    m = v.m
    cells = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where = -1e9, None
    n = 0
    for state in STATE_NAMES:
        for th in thetas(24):
            n += 1
            for _, el in v.state_els((th, state)):
                lo, hi = el.aabb()
                o = box_overhang(lo, hi, cells)
                if o > worst:
                    worst, where = o, (el.name, th, state)
    print(f"containment over {n} poses (four states): worst overhang {worst:.3f} voxels ({where[0]})")
    if worst > 0.01:
        v.fail(f"{where[0]} leaves the machine box at theta {where[1]} in {where[2]}")


# ---------------------------------------------------------------- the ports and the power
FACE_AXIS = {"west": (0, 0), "east": (0, 1), "down": (1, 0), "up": (1, 1), "north": (2, 0), "south": (2, 1)}


def on_face(cell, face, p, tol=1e-3):
    axis, hi = FACE_AXIS[face]
    plane = (cell[axis] + hi) * 16
    if abs(p[axis] - plane) > tol:
        return False
    return all(cell[k] * 16 - tol <= p[k] <= (cell[k] + 1) * 16 + tol for k in range(3) if k != axis)


def check_ports(v):
    """The power and the four ports: the entry shaft meets the power face at the power cell's centre; each
    port's point is on its cell's face, that face on the outside of the footprint; the feed comes in on the
    top row, the outlets leave on the bottom one; no two share a face; each outlet chute's floor reaches its
    face under the port's point, within its sides, and the inlet's floor lies under the feed point."""
    m, rig = v.m, v.rig
    pc = tuple(rig["powerCell"])
    entry = v.named("entry", r"_shaft")
    lo, _ = aabb_of(entry)
    cx, cy = m.ENTRY
    ok = lo[2] <= 0.01 and pc == m.POWER_CELL and rig["powerFace"] == "north" and (cx % 16, cy % 16) == (8.0, 8.0) \
        and (cx // 16, cy // 16) == pc[:2] and pc[2] == 0
    print(f"power: cell {pc} face {rig['powerFace']}; the entry shaft starts at z {lo[2]:.2f}, its axis at (x {cx}, y {cy}): the cell's centre")
    if not ok:
        v.fail("the entry shaft does not meet the power face at the cell's centre")
    faces = {(tuple(rig["powerCell"]), rig["powerFace"])}
    dims = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    for key in m.ANCHORS:
        cell, face = tuple(rig[f"{key}Cell"]), rig[f"{key}Face"]
        p = [c * 16 for c in rig[key]["pos"]]
        axis, hi = FACE_AXIS[face]
        outside = cell[axis] == (dims[axis] - 1 if hi else 0)
        row = cell[1]
        want_row = m.CELLS_Y - 1 if key == "feed" else 0
        print(f"port {key}: cell {list(cell)} {face} face, point {[round(c, 2) for c in p]}")
        if not on_face(cell, face, p):
            v.fail(f"{key}'s point is not on its cell's {face} face")
        if not outside:
            v.fail(f"{key}'s face is inside the footprint")
        if row != want_row:
            v.fail(f"{key} is on row {row}, want {want_row}")
        if (cell, face) in faces:
            v.fail(f"{key} shares its face with another port or the power")
        faces.add((cell, face))
    # each chute's floor at its port: the port's point stands over it, within its sides
    for key, pid, rx_floor in (("fines", "frame", r"fr_bin_spoutfloor"), ("oversize", "tailspout", r"_floor2"),
                               ("oversize", "discharge", r"_floor2"), ("return", "return", r"_floor"), ("feed", "frame", r"fr_inlet_floor")):
        p = [c * 16 for c in rig[key]["pos"]]
        state = {"tailspout": "tier2", "discharge": "tier3", "return": "tier4"}.get(pid, "tier4")
        floor = v.named(pid, rx_floor, 0.0)
        hit = ray_down([p[0] - (0.05 if key == "oversize" else -0.3 if key == "feed" else 0.0), p[1], p[2] + (0.05 if key == "fines" else -0.05 if key == "return" else 0.0)],
                       [e for _, e in v.state_els((0.0, state))])
        good = hit is not None and hit[0].name in {e.name for e in floor} and hit[1] < 2.5
        print(f"port {key} ({pid}): under its point {('nothing' if hit is None else f'{hit[0].name} {hit[1]:.2f} below')}")
        if not good:
            v.fail(f"{key}'s chute ({pid}) does not reach its face under the port's point")


# ---------------------------------------------------------------- textures by role
TEX_RULES = [
    (r"^(fr_post|fr_sill|fr_girt|fr_rail|fr_endtie|fr_beam|fr_westtie|fr_knee|fr_bin_rim|fr_bin_spoutfoot|entry_shaft[ab]|mount_timberw|"
     r"(tailspout|discharge|return)_leg)", "oak"),
    (r"^(fr_inlet_(floor|side)|fr_bin_(floor|side|spout)|screen_(side|headboard)|(tailspout|discharge|return)_(floor|side|step|back))", "planks"),
    (r"^(fr_inlet_lip|fr_bearing|grizzly_|screen_(bar|pin|rodpin|lug)|hanger\d_|brackets_|ecc[ns]_|rod[ns]_|drum_(feedring|midring|lipring|spider)|"
     r"mount_(bearing|pedestal|hanger))", "iron"),
    (r"^(entry_shaft_|drum_shaft|wheel_|pinion_)", "steel"),
    (r"^(screen_(feedplate|tailplate)|drum_band|mount_feed)", "sheet"),
    (r"^(screen_deck|drum_jacket)", "mesh"),
]


def check_textures(v):
    bad, unruled = [], []
    for el in v.els:
        for rx, tex in TEX_RULES:
            if re.match(rx, el.name):
                got = {f["texture"].lstrip("#") for f in el.faces.values()}
                if got != {tex}:
                    bad.append((el.name, sorted(got), tex))
                break
        else:
            unruled.append(el.name)
    print(f"textures: {len(TEX_RULES)} rules (oak timbers and the axle's continuation, plank chutes, bin and screen sides, iron "
          f"castings, bars and fittings, steel shafts and gears, sheet-iron blank plates, a wire-cloth deck and jacket): "
          f"{len(bad)} elements break them, {len(unruled)} have no rule")
    if bad:
        v.fail(f"textures by role: {bad[:4]}")
    if unruled:
        v.fail(f"elements with no texture rule: {unruled[:6]}")


# ---------------------------------------------------------------- the bevel pair
def velocity(v, pid, theta, p, h=1e-4):
    a = v.point(pid, theta, p)
    b = v.point(pid, theta + h, p)
    return [(b[k] - a[k]) / h for k in range(3)]


def check_gearing(v):
    """The bevel pair: both axes through the apex; the pitch circles' radii in the teeth's ratio and each the
    other's distance from the apex (one pitch cone distance); the pitch point moving alike on both gears
    (finite differences of the posed rig); the drum turning a third of a turn a turn; its tilt held."""
    m = v.m
    apex = m.APEX
    # the pitch point at rest, tilted with the pair
    q = m.drum_axis_point(apex[0] - m.R_PINION)
    q = (q[0], q[1], q[2] - m.R_WHEEL)
    dist = math.dist(q, apex)
    cone = math.hypot(m.R_PINION, m.R_WHEEL)
    print(f"bevel: pinion {m.PINION_TEETH}, wheel {m.WHEEL_TEETH} (module {m.MODULE}), pitch radii {m.R_PINION}, {m.R_WHEEL}; "
          f"the pitch point {dist:.4f} from the apex (cone distance {cone:.4f})")
    if abs(dist - cone) > 1e-9 or abs(m.R_PINION / m.R_WHEEL - m.PINION_TEETH / m.WHEEL_TEETH) > 1e-12:
        v.fail("the bevel pair's pitch cones do not share their cone distance")
    # the material points of each gear at the pitch point, at several angles, move alike
    worst = 0.0
    for th in [0.0] + thetas(12):
        pp, pw = inverse_point(v.mat("pinion", th), q), inverse_point(v.mat("wheel", th), q)
        vp, vw = velocity(v, "pinion", th, pp), velocity(v, "wheel", th, pw)
        size = max(math.sqrt(sum(c * c for c in vp)), 1e-9)
        worst = max(worst, math.sqrt(sum((vp[k] - vw[k]) ** 2 for k in range(3))) / size)
    print(f"bevel: the pitch point moves alike on both gears at 13 angles (worst relative difference {worst:.1e})")
    if worst > 1e-3:
        v.fail("the bevel pair's pitch point slips")
    # the drum turns about its own (inclined) axis, a third of a turn a turn, the other way to the axle
    far = m.drum_axis_point(20.0)
    held = max(math.dist(v.point("drum", th, p), p) for th in thetas(6) for p in (apex, far))
    ang = drum_turn(v, math.tau * 3)
    print(f"trommel: its axis stays put as it turns (a point on it moves {held:.1e}); three axle turns turn it {ang / math.tau:+.4f} "
          f"turns; slope 1 in {1 / math.tan(m.TILT):.1f}")
    if held > 1e-4 or abs(ang + math.tau) > 1e-4:
        v.fail("the trommel does not turn about its own axis a third of a turn a turn")


def inverse_point(mat, p):
    """The authored point that `mat` (a part matrix, blocks) moves to p (voxels)."""
    r = [row[:3] for row in mat[:3]]
    t = [mat[i][3] * 16 for i in range(3)]
    d = [p[i] - t[i] for i in range(3)]
    return [sum(r[j][i] * d[j] for j in range(3)) for i in range(3)]


def drum_turn(v, theta, steps=96):
    """The drum's total turn about its own axis (radians, right-handed about its axis towards the east) at
    theta: its matrix levelled (turned back by the tilt either side) is a turn about x, followed in steps."""
    m = v.m
    c, s = math.cos(m.TILT), math.sin(m.TILT)
    rz = [[c, -s, 0.0], [s, c, 0.0], [0.0, 0.0, 1.0]]          # the level frame from the tilted: about z by +TILT
    rzt = [[c, s, 0.0], [-s, c, 0.0], [0.0, 0.0, 1.0]]
    total, prev = 0.0, None
    for i in range(steps + 1):
        mat = v.mat("drum", theta * i / steps)
        r = [row[:3] for row in mat[:3]]
        lv = mm3(rz, mm3(r, rzt))
        ang = math.atan2(lv[2][1], lv[1][1])
        if prev is not None:
            total += math.remainder(ang - prev, math.tau)
        prev = ang
    return total


def mm3(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


# ---------------------------------------------------------------- the linkage
def check_linkage(v):
    """Tier 2's linkage, every pin on its pin: each rod's big end on its eccentric's centre and its small end
    on the screen's pin; each hanger's top on its bracket's pin and its foot on the screen's; the screen moves
    without turning, its stroke the eccentric's throw twice (or near it)."""
    m = v.m
    e0 = m.ecc_centre(0.0)
    worst = {"rod big end": 0.0, "rod small end": 0.0, "hanger top": 0.0, "hanger foot": 0.0, "screen turn": 0.0}
    xs = []
    for th in thetas(48, turns=1):
        for side in ("n", "s"):
            ecc = v.point(f"ecc{side}", th, (*e0, m.ZD))
            rod_big = v.point(f"rod{side}", th, (*e0, m.ZD))
            rod_small = v.point(f"rod{side}", th, (*m.ROD_PIN, m.ZD))
            scr = v.point("screen", th, (*m.ROD_PIN, m.ZD))
            worst["rod big end"] = max(worst["rod big end"], math.dist(ecc, rod_big))
            worst["rod small end"] = max(worst["rod small end"], math.dist(rod_small, scr))
        for i, (top, foot) in enumerate(m.hanger_pins(), 1):
            for k in (2 * i - 1, 2 * i):
                worst["hanger top"] = max(worst["hanger top"], math.dist(v.point(f"hanger{k}", th, (*top, m.ZD)), (*top, m.ZD)))
                worst["hanger foot"] = max(worst["hanger foot"], math.dist(v.point(f"hanger{k}", th, (*foot, m.ZD)),
                                                                        v.point("screen", th, (*foot, m.ZD))))
        mat = v.mat("screen", th)
        worst["screen turn"] = max(worst["screen turn"], max(abs(mat[r][c] - (1.0 if r == c else 0.0)) for r in range(3) for c in range(3)))
        xs.append(mat[0][3] * 16)
    stroke = max(xs) - min(xs)
    print("linkage over a turn: " + ", ".join(f"{k} off by {w:.1e}" for k, w in worst.items()) + f"; the screen's stroke {stroke:.3f} "
          f"(the eccentric's throw twice: {2 * m.ECC})")
    if max(worst.values()) > 2e-3 or abs(stroke - 2 * m.ECC) > 0.1:
        v.fail("tier 2's linkage comes apart at a pin")


# ---------------------------------------------------------------- supports
def check_supports(v):
    """Every shaft in two bearings: the entry shaft in the two pillow blocks on the girts; the drum's shaft
    (looked at level) in the west and east pillow blocks; each rod's eye round its pin and each hanger
    between its pins (the linkage check holds them there as they move)."""
    m = v.m
    frame = v.by_part["frame"]
    found, _ = shaft_supports(frame, v.named("entry", r"_shaft_"), 2, m.ENTRY)
    named = sorted(n for n in found if re.match(r"fr_bearing\d", n))
    print(f"support entry shaft: {len(named)} bearing pieces {', '.join(named) or 'NONE'}")
    if len({n[:11] for n in named}) < 2:
        v.fail("the entry shaft is not carried by two bearings")
    # the drum's shaft, levelled again about the apex
    def level(els):
        out = [e.clone() for e in els]
        return rotate(out, "z", math.degrees(m.TILT), m.APEX)
    shaft = level(v.named("drum", r"_shaft"))
    mount = level(v.named("mount", r"_bearing"))
    found, _ = shaft_supports(mount, shaft, 0, (m.APEX[1], m.APEX[2]))
    blocks = sorted({re.sub(r"\d+$", "", n) for n in found})
    print(f"support drum shaft: in {', '.join(blocks) or 'NONE'}")
    if len(blocks) < 2:
        v.fail("the drum's shaft is not carried by two bearings")


# ---------------------------------------------------------------- clearances
# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way
# round. Everything else must clear by more than 0.02 voxels at every sampled pose.
ALLOWED = [
    # shafts in their bearings, fittings keyed on them
    ("entry", r"_shaft", "frame", r"fr_bearing\d"),
    ("ecc[ns]", None, "entry", None), ("pinion", None, "entry", None),
    ("drum", r"_shaft", "mount", r"_bearing"), ("wheel", None, "drum", r"_shaft"),
    # the bevel pair (its pitch circles are checked by check_gearing)
    ("pinion", None, "wheel", None),
    # the rods: straps round the sheaves, eyes round the screen's pins
    ("rod[ns]", r"_strap", "ecc[ns]", r"_sheave"), ("rod[ns]", r"_eye", "screen", r"_rodpin"),
    # the hangers on their pins
    ("hanger\\d", None, "screen", r"_pin[ns]\d"), ("hanger\\d", None, "brackets", r"_pin"),
    # fixed parts on the frame: the brackets and straps into the beams, the grizzly's heads under the inlet's lip,
    # the trommel's timber on the girts and its hanger in the beam, the feed chute under the inlet's lip, the spouts'
    # and the return chute's legs and ends on the sills
    ("brackets", None, "frame", r"fr_beam"), ("grizzly", r"_strap", "frame", r"fr_beam"),
    ("grizzly", r"_bar", "frame", r"fr_inlet"),
    ("mount", r"_timberw", "frame", r"fr_girt|fr_post_w"), ("mount", r"_hangere", "frame", r"fr_beam3"),
    ("mount", r"_feed", "frame", r"fr_inlet"),
    ("tailspout|discharge", r"_(floor2|side[ns]2)", "frame", r"fr_sill5"),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose):
    items = [(pid, el, el.aabb()) for pid, el in v.state_els(pose)]
    grid = {}
    for i, (pid, el, (lo, hi)) in enumerate(items):
        for cx in range(int(lo[0] // 4), int(hi[0] // 4) + 1):
            for cy in range(int(lo[1] // 4), int(hi[1] // 4) + 1):
                for cz in range(int(lo[2] // 4), int(hi[2] // 4) + 1):
                    grid.setdefault((cx, cy, cz), []).append(i)
    hits = {}
    seen = set()
    for idx in grid.values():
        for a in range(len(idx)):
            for b in range(a + 1, len(idx)):
                i, j = idx[a], idx[b]
                if (i, j) in seen:
                    continue
                seen.add((i, j))
                pa, ea, (alo, ahi) = items[i]
                pb, eb, (blo, bhi) = items[j]
                if pa == pb:
                    continue
                if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                    continue
                if allowed(pa, ea.name, pb, eb.name):
                    continue
                if obb_obb(ea, eb):
                    hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def clearance_poses():
    out = []
    for state in STATE_NAMES:
        n = 6 if state == "frame" else 36
        out += [(th, state) for th in thetas(n, turns=3 if state != "tier2" else 1)]
    return out


def check_clearances(v, poses):
    allhits = {}
    for pose in poses:
        for key, hs in touching_pairs(v, pose).items():
            allhits.setdefault(key, {})
            for h in hs:
                allhits[key].setdefault(h, pose)
    print(f"clearances over {len(poses)} poses (each state over its cycle): {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {p[0]:.3f} {p[1]}" for (a, b), p in ex))
    if allhits:
        v.fail("parts run into each other")


# ---------------------------------------------------------------- where material falls
def ray_down(p, els):
    """The first element a ray straight down from p meets, and how far down: (element, distance), or None."""
    best = None
    for e in els:
        lo, hi = e.aabb()
        if not (lo[0] - 1e-9 <= p[0] <= hi[0] + 1e-9 and lo[2] - 1e-9 <= p[2] <= hi[2] + 1e-9 and lo[1] < p[1]):
            continue
        # the ray in the element's local frame
        o = [p[k] - e.c[k] for k in range(3)]
        lo_l = [sum(e.r[k][i] * o[k] for k in range(3)) for i in range(3)]
        d_l = [-e.r[1][i] for i in range(3)]
        t0, t1 = 0.0, 1e9
        for i in range(3):
            h = abs(e.size[i]) / 2
            if abs(d_l[i]) < 1e-12:
                if abs(lo_l[i]) > h:
                    break
                continue
            ta, tb = (-h - lo_l[i]) / d_l[i], (h - lo_l[i]) / d_l[i]
            if ta > tb:
                ta, tb = tb, ta
            t0, t1 = max(t0, ta), min(t1, tb)
            if t0 > t1:
                break
        else:
            if best is None or t0 < best[1]:
                best = (e, t0)
    return best


FLOWS = [
    # (state, what falls, where from (a function of the model giving points), what must take it: element regex)
    ("tier2", "the feed off the inlet's lip onto the grizzly's bars", "inlet_bars", r"^grizzly_bar"),
    ("tier2", "the grizzly's fines between its bars onto the screen", "grizzly_gaps", r"^screen_(feedplate|deck)"),
    ("tier2", "the grizzly's lumps off its foot onto the screen", "grizzly_foot", r"^screen_(feedplate|deck)"),
    ("tier2", "the screen's fines through its deck into the bin", "deck", r"^fr_bin_floor"),
    ("tier2", "the screen's oversize off its tail into its spout", "screen_tail", r"^tailspout_floor1"),
    ("tier3", "the feed off the inlet's lip into the feed chute", "inlet_lip", r"^mount_feed(floor|wing)$"),
    ("tier3", "the feed off the feed chute into the drum", "feed_lip", r"^drum_(jacket|feedring)"),
    ("tier3", "the drum's fines through its jacket into the bin", "jacket", r"^fr_bin_floor"),
    ("tier3", "the drum's oversize off its lip into the discharge spout", "drum_lip", r"^discharge_floor1"),
    ("tier4", "the drum's oversize off its lip into the return chute", "drum_lip", r"^return_floor"),
]


def flow_points(m, v, what, theta):
    """Sample points (voxels) a little under where material leaves each surface, at theta."""
    zs = [m.ZD + dz for dz in (-3.0, -1.0, 0.5, 2.5)]
    z_bars = [m.ZD + (i - (m.GRIZ_BARS - 1) / 2) * (m.GRIZ_BAR_W + m.GRIZ_GAP) for i in range(m.GRIZ_BARS)]
    (_, _), (lx, ly) = m.INLET_FLOOR
    if what == "inlet_bars":
        return [(lx + 0.3, ly - 0.05, z) for z in z_bars]
    if what == "inlet_lip":
        return [(lx + 0.3, ly - 0.05, m.ZD + dz) for dz in (-3.6, -2.0, -0.5, 1.0, 2.5, 3.6)]
    if what == "grizzly_gaps":
        gaps = [(a + b) / 2 for a, b in zip(z_bars, z_bars[1:])]
        out = []
        for s in (1.5, 4.5, 7.5, 10.0):
            x, y = m.griz_point(s, m.GRIZ_DEPTH + 0.3)
            out += [(x, y, z) for z in gaps]
        return out
    if what == "grizzly_foot":
        x, y = m.griz_point(m.GRIZ_LEN + 0.5, 0.0)
        return [(x + 0.3, y - 1.4, z) for z in zs]
    if what == "deck":
        dx = v.mat("screen", theta)[0][3] * 16
        out = []
        for x in (m.MESH_X[0] + 1.0, 18.0, 26.0, 33.0, m.MESH_X[1] - 1.0):
            y = m.deck_y(x) - m.DECK_T - 0.9 + v.mat("screen", theta)[1][3] * 16
            out += [(x + dx, y, z) for z in zs]
        return out
    if what == "screen_tail":
        dx = v.mat("screen", theta)[0][3] * 16
        x = m.SCREEN_TAIL[0] + dx + 0.4
        return [(x, m.SCREEN_TAIL[1] - 0.5, z) for z in zs]
    if what == "feed_lip":
        _, _, (x1, y1) = m.feed_valley()
        z0, z1 = m.FEED_CHUTE_Z
        return [(x1 + 0.3, y1 - 0.05, z) for z in (z0 + 0.3, (z0 + z1) / 2, z1 - 0.3)]
    if what == "jacket":
        out = []
        for xl in (16.0, 22.0, 33.0, 39.0):
            a = m.drum_axis_point(xl)
            out += [(a[0], a[1] - m.DRUM_R - m.JACKET_T / 2 - 0.3, z) for z in (m.ZD - 2.0, m.ZD, m.ZD + 2.0)]
        return out
    if what == "drum_lip":
        a = m.drum_axis_point(m.DRUM_X[1] + 0.9)
        return [(a[0], a[1] - m.LIP_RING_IN + 0.2, z) for z in (m.ZD - 3.0, m.ZD, m.ZD + 3.0)]
    raise ValueError(what)


def check_flow(v):
    """Where material falls: from each surface it leaves, straight down, the first thing below is the surface
    meant to take it (at several angles, as the screen and the drum move): feed, grizzly, screen, bin, spouts,
    feed chute, drum, discharge spout and return chute. Clear paths, by a ray, not by eye."""
    m = v.m
    n = 0
    for state, label, what, rx in FLOWS:
        bad = []
        for th in thetas(6, turns=1 if state == "tier2" else 3):
            els = [e for _, e in v.state_els((th, state))]
            for p in flow_points(m, v, what, th):
                n += 1
                hit = ray_down(p, els)
                if hit is None or not re.search(rx, hit[0].name):
                    bad.append((round(th, 2), [round(c, 2) for c in p], None if hit is None else hit[0].name))
        print(f"flow {state}: {label}: {'clear' if not bad else f'{len(bad)} points miss, e.g. {bad[:2]}'}")
        if bad:
            v.fail(f"{state}: {label}: material falls elsewhere")
    print(f"flow: {n} drops")


# ---------------------------------------------------------------- z-fighting
def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = [e for _, e in v.state_els(pose)]
        pairs = coplanar_faces(els)
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses (each with its state's parts)")
    if bad:
        v.fail("faces z-fight")


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_floating(v)
    check_containment(v)
    check_ports(v)
    check_textures(v)
    check_gearing(v)
    check_linkage(v)
    check_supports(v)
    check_flow(v)
    check_clearances(v, clearance_poses())
    if not quick:
        check_zfight(v)
    return v.ok


def validate_files(m, shape, frame_shape, ship):
    ok = True
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        print(f"FAIL textures used but not declared: {missing}")
        ok = False
    gaps = lid_gaps(ship["cells"])
    hollow = [c["pos"] for c in ship["cells"] if c.get("hollow")]
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, hollow {hollow}; "
          f"lids over every column: {'yes' if not gaps else gaps}")
    if gaps:
        ok = False
    return ok
