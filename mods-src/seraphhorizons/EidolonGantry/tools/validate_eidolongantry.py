"""The eidolon gantry generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model
breaks its rule. Everything is in the build frame (voxels), which is also the shipped frame divided
by 16 (the controller cell is the build frame's corner cell).
"""

from __future__ import annotations

import math
import re

import kin
from machinegen.checks import box_overhang, coplanar_faces, euler_round_trip, frame_floating, touching
from machinegen.geometry import El, flatten
from machinegen.rigmath import part_of, posed

TOL = 0.01                                    # voxels: the baked body against kin's hung pose, the floor
GANTRY = ("frame", "winch", "sheave", "lead", "fall", "hook", "ring")


class V:
    def __init__(self, m, els, parts):
        self.m, self.els, self.parts = m, els, parts
        self.by_part = {}
        for el in els:
            self.by_part.setdefault(el.part, []).append(el)
        self.ok = True

    def fail(self, msg):
        self.ok = False
        print("FAIL", msg)

    def posed(self, pids, depth):
        return [posed(el, self.m.pm(self.parts, pid, depth)) for pid in pids for el in self.by_part.get(pid, [])]


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
    print("parts: " + ", ".join(f"{p['id']} {len(v.by_part.get(p['id'], []))}" for p in v.parts))


def check_baked(v, body_shape):
    """The baked body is the hung pose: every element's corners where kin (the game's maths) puts them."""
    rig, mats = v.m.hung_matrices(body_shape)
    worst = 0.0
    for code in v.m.body_codes(v.m.load_body()[1]):
        for el in v.by_part.get(code, []):
            name = el.name[len(f"b_{code}_"):]
            want = v.m.body_corners(rig, mats, name)
            got = el.corners()
            worst = max(worst, max(min(math.dist(p, q) for q in got) for p in want))
    print(f"baked body against kin's hung pose: worst corner {worst:.2e} voxels")
    if worst > TOL:
        v.fail("the baked body is not the hung pose")


def check_stages(v, body_shape, stages):
    """Every drawn element of the eidolon is in the gantry once, in its stage's part; after every stage the
    body shown so far is one piece (the eidolon's own rule: the torso comes before the pelvis it hangs from
    in the shape's hierarchy, which the baked pose makes harmless, but nothing may float); the first stage
    holds the spine's peg, so the ring, fitted with it, holds the body from the first part on."""
    st = v.m.stage_of(stages)
    rig = kin.Rig(body_shape)
    drawn = [n for n in rig.order if v.m.drawn(rig.elements[n])]
    faceless = sorted(n for n in rig.order if not v.m.drawn(rig.elements[n]))
    missing = [n for n in rig.order if n not in st]
    if missing:
        v.fail(f"eidolon elements in no stage: {missing[:6]}")
    have = {el.name for code in v.m.body_codes(stages) for el in v.by_part.get(code, [])}
    want = {f"b_{st[n]}_{n}" for n in drawn if n in st}
    if have != want:
        v.fail(f"stage coverage: missing {sorted(want - have)[:6]}, extra {sorted(have - want)[:6]}")
    shown = set()
    for s in stages["stages"]:
        shown |= set(s["elements"])
        tops = [n for n in rig.order if n in shown and rig.parent[n] not in shown]
        if len(tops) > 1:
            v.fail(f"after {s['code']} the body is in {len(tops)} pieces, from {tops[:6]}")
    first = v.m.body_codes(stages)[0]
    if st.get("spine-hook1") != first:
        v.fail(f"the spine's peg (spine-hook1) is not in the first stage, {first}")
    ring = next(p for p in v.parts if p["id"] == "ring")
    if ring["requires"] != first:
        v.fail(f"the ring requires {ring['requires']!r}, not the first stage {first!r}")
    counts = {code: len(v.by_part.get(code, [])) for code in v.m.body_codes(stages)}
    print(f"stages: {counts}; {len(have)} of {len(rig.order)} eidolon elements (left out, no drawn face: {faceless})")


def check_floor(v, stages):
    body = list(v.m.body_codes(stages))
    for depth, want in ((0.0, v.m.drop()), (1.0, 0.0)):
        low = v.m.lowest(v.posed(body, depth))
        print(f"floor: the body's lowest point at depth {depth}: {low:.3f} (want {want:.3f})")
        if abs(low - want) > TOL:
            v.fail(f"the body's lowest point at depth {depth} is {low:.3f}, not {want:.3f}")
    if abs(v.m.drop() - 3.0) > TOL:
        v.fail(f"the hung toe is {v.m.drop():.3f} above the floor, not the eidolon's 3 (HUNG_CLEAR)")


def check_containment(v):
    box = (v.m.CELLS_X, v.m.CELLS_Y, v.m.CELLS_Z)
    worst, where = -1e9, None
    for depth in (0.0, 0.5, 1.0):
        for el in v.posed(list(v.by_part), depth):
            lo, hi = el.aabb()
            o = box_overhang(lo, hi, box)
            if o > worst:
                worst, where = o, el.name
    print(f"containment: worst overhang {worst:.2f} voxels ({where})")
    if worst > 0.01:
        v.fail(f"{where} pokes {worst:.2f} voxels out of the machine box")


def check_clearances(v, stages):
    """The body touches nothing of the gantry but the ring's bottom bar, under the peg."""
    body = list(v.m.body_codes(stages))
    for depth in (0.0, 0.5, 1.0):
        fixed = v.posed(["frame", "winch", "sheave", "lead", "fall", "hook"], depth)
        hits = touching(v.posed(body, depth), fixed)
        ring = touching(v.posed(body, depth), v.posed(["ring"], depth))
        ring = {h for h in ring if not (h[0] == "b_torso_spine-hook1" and h[1] == "rg_bottom")}
        print(f"clearances at depth {depth}: body-gantry {len(hits)}, body-ring {len(ring)}")
        for what, h in (("the body touches the gantry", hits), ("the ring touches the body", ring)):
            if h:
                v.fail(f"{what} at depth {depth}: {sorted(h)[:6]}")


def check_ring(v, body_shape):
    """The peg passes through the ring: the ring's inside (y between its bars, z between its sides) holds
    the peg's cross-section where the ring is, and the ring's bottom bar is under it."""
    m = v.m
    p, slope, size = m.hang_point(body_shape)
    ring = {el.name: el.aabb() for el in v.by_part["ring"]}
    zin = (ring["rg_side_r"][1][2], ring["rg_side_l"][0][2])
    yin = (ring["rg_bottom"][1][1], ring["rg_top"][0][1])
    peg_z = (p[2] - size[2] / 2, p[2] + size[2] / 2)
    under = p[1] - abs(slope) * m.RING_X / 2 - size[1] * math.sqrt(1 + slope * slope)
    print(f"ring: inside z {zin[0]:.2f}..{zin[1]:.2f} round the peg's {peg_z[0]:.2f}..{peg_z[1]:.2f}; "
          f"bottom bar at {yin[0]:.3f}, the peg's lowest underside {under:.3f}; top bar {yin[1] - p[1]:.2f} above the peg")
    if not (zin[0] < peg_z[0] and peg_z[1] < zin[1]) or abs(yin[0] - under) > TOL or yin[1] <= p[1] + abs(slope) * m.RING_X / 2:
        v.fail("the peg is not held in the ring")


def check_chains(v):
    """The lead chain is tangent to the drum and the sheave, the fall leaves the sheave's front at the
    tangent and ends on the eye at every depth, and the drum, the sheave and the chain move together."""
    m = v.m
    (ax, ay), (bx, by) = m.lead_tangents()
    u = ((bx - ax), (by - ay))
    n = math.hypot(*u)
    u = (u[0] / n, u[1] / n)
    dr = (ax - m.DRUM[0], ay - m.DRUM[1])
    sr = (bx - m.sheave_x(), by - m.SHEAVE_Y)
    errs = [abs(math.hypot(*dr) - m.DRUM_R), abs(math.hypot(*sr) - m.SHEAVE_R),
            abs(dr[0] * u[0] + dr[1] * u[1]), abs(sr[0] * u[0] + sr[1] * u[1])]
    lead = v.by_part["lead"][0]
    ends = [[lead.c[k] + s * lead.r[k][1] * lead.size[1] / 2 for k in range(3)] for s in (-1, 1)]
    errs.append(max(math.dist(ends[0][:2], (ax, ay)), math.dist(ends[1][:2], (bx, by))))
    # paying out turns the drum's surface at its tangent along the chain, and the sheave's
    for c, r, amount in ((m.DRUM, m.DRUM_R, m.drop() / m.DRUM_R), ((m.sheave_x(), m.SHEAVE_Y), m.SHEAVE_R, m.drop() / m.SHEAVE_R)):
        t = (ax, ay) if c == m.DRUM else (bx, by)
        vel = (-(t[1] - c[1]), t[0] - c[0])            # d/dangle of the tangent point about +z
        if vel[0] * u[0] + vel[1] * u[1] <= 0:
            v.fail("the drum or the sheave turns against the chain as it pays out")
        errs.append(abs(amount * r - m.drop()))
    worst = 0.0
    for depth in (0.0, 0.25, 0.5, 0.75, 1.0):
        fall = v.posed(["fall"], depth)[0]
        eye = v.posed(["hook"], depth)[0]
        flo, fhi = fall.aabb()
        elo, ehi = eye.aabb()
        worst = max(worst, abs(flo[1] - ehi[1]), abs(fhi[1] - m.SHEAVE_Y), abs((flo[0] + fhi[0]) / 2 - (m.sheave_x() - m.SHEAVE_R)))
    errs.append(worst)
    print(f"chains: tangents, ends and pay-out within {max(errs):.2e} voxels")
    if max(errs) > TOL:
        v.fail("a chain is off its drum, sheave or eye")


def check_supports(v):
    """Nothing floats: the frame is one piece from the ground, the winch's axle runs in both cheeks and
    the sheave's pin in both hangers."""
    seen, loose = frame_floating(v.by_part["frame"])
    print(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")
    frame = {el.name: el.aabb() for el in v.by_part["frame"]}

    def held(shaft, holders):
        lo, hi = shaft
        return all(lo[2] < frame[h][0][2] and frame[h][1][2] < hi[2] for h in holders)
    axle = [el.aabb() for el in v.by_part["winch"] if el.name.startswith("wn_axle")][0]
    pin = [el.aabb() for el in v.by_part["sheave"] if el.name.startswith("sv_pin")][0]
    cheeks = [n for n in frame if n.startswith("fr_cheek_")]
    if not held(axle, ["fr_bearing_r", "fr_bearing_l"]) or not all(axle[0][2] < frame[c][1][2] and frame[c][0][2] < axle[1][2] for c in cheeks):
        v.fail("the winch's axle does not run in both cheeks")
    if not held(pin, ["fr_hanger1", "fr_hanger2"]):
        v.fail("the sheave's pin does not run in both hangers")


def check_exit(v, body_shape):
    """The front is open: the eidolon standing where it hung (vanilla's rest pose) walks straight out of
    the west side touching nothing of the frame, the winch or the hung chains, its whole height."""
    m = v.m
    rig = kin.Rig(body_shape)
    mats = rig.all_matrices({})
    standing = []
    for n in rig.order:
        e = rig.elements[n]
        if not m.drawn(e):
            continue
        size = [e["to"][i] - e["from"][i] for i in range(3)]
        c = kin.apply(mats[n], [s / 2 for s in size])
        standing.append(El(n, size, [c[k] + m.OFF[k] for k in range(3)], [list(r) for r in kin.rotation_of(mats[n])], {}, None))
    gantry = v.posed(["frame", "winch", "sheave", "lead", "fall", "hook"], 0.0)
    hits = set()
    step = 2.0
    shift = 0.0
    while shift <= m.BODY_AT[0] + 20.0:
        moved = []
        for el in standing:
            e2 = el.clone()
            e2.c = [el.c[0] - shift, el.c[1], el.c[2]]
            moved.append(e2)
        hits |= touching(moved, gantry)
        shift += step
    lo = min(min(c[2] for c in el.corners()) for el in standing)
    hi = max(max(c[2] for c in el.corners()) for el in standing)
    top = max(max(c[1] for c in el.corners()) for el in standing)
    print(f"exit: the standing body (z {lo:.1f}..{hi:.1f}, {top:.1f} tall) walks out west through the opening z "
          f"{m.Z_RIGHT[1]:.0f}..{m.Z_LEFT[0]:.0f} under the front beam at {m.HEAD[0]:.0f}: {len(hits)} contacts")
    if hits:
        v.fail(f"the way out is blocked: {sorted(hits)[:6]}")


def check_room(v, stages):
    """Room to walk round the hung body and fit parts: the gaps between it and the posts, the winch."""
    m = v.m
    body = v.posed(list(m.body_codes(stages)), 0.0)
    lo = [min(min(c[k] for c in el.corners()) for el in body) for k in range(3)]
    hi = [max(max(c[k] for c in el.corners()) for el in body) for k in range(3)]
    gaps = {"front": lo[0] - m.X_FRONT[1], "back (to the winch)": m.CHEEK_X[0] - hi[0],
            "right": lo[2] - m.Z_RIGHT[1], "left": m.Z_LEFT[0] - hi[2], "overhead": m.SHEAVE_Y - m.FLANGE_R - hi[1]}
    print("room round the hung body (blocks): " + ", ".join(f"{k} {g / 16:.2f}" for k, g in gaps.items()))
    small = [k for k, g in gaps.items() if k != "overhead" and g < 16.0]
    if small:
        v.fail(f"less than a block to walk past the body: {small}")
    if gaps["overhead"] < 1.0:
        v.fail("the sheave is on the body's head")


ROLES = [
    (r"^fr_(post|head|beam|hoist|sill|rail|knee|cheek)", {"oak"}),
    (r"^fr_(iron|bearing|hanger)", {"iron"}),
    (r"^wn_(axle|hoop|crank)", {"iron"}),
    (r"^wn_(drum|handle)", {"oak"}),
    (r"^wn_coil|^ld_|^fl_", {"chain"}),
    (r"^sv_pin|^hk_|^rg_", {"iron"}),
    (r"^sv_(hub|flange)", {"oak"}),
]


def check_textures(v):
    bad = []
    for el in v.els:
        if el.part not in GANTRY:
            continue
        tex = {f["texture"].lstrip("#") for f in el.faces.values()}
        rule = next((want for rx, want in ROLES if re.match(rx, el.name)), None)
        if rule is None or not tex <= rule:
            bad.append((el.name, sorted(tex), sorted(rule or [])))
    print(f"textures by role: {sum(el.part in GANTRY for el in v.els) - len(bad)} gantry elements as their role says")
    if bad:
        v.fail(f"textures off their role: {bad[:6]}")


def check_zfight(v):
    worst = 0
    for depth in v.m.COPLANAR_POSES:
        pairs = coplanar_faces(v.posed(list(GANTRY), depth))
        worst = max(worst, len(pairs))
        if pairs:
            v.fail(f"z-fighting at depth {depth}: {[(a, b) for a, _, b, _, _ in pairs[:4]]}")
    print(f"z-fighting (the gantry's own faces): {worst} pairs after the fix")


def validate(m, els, parts, rig, body_shape, stages):
    v = V(m, els, parts)
    check_basic(v)
    check_baked(v, body_shape)
    check_stages(v, body_shape, stages)
    check_floor(v, stages)
    check_containment(v)
    check_clearances(v, stages)
    check_ring(v, body_shape)
    check_chains(v)
    check_supports(v)
    check_exit(v, body_shape)
    check_room(v, stages)
    check_textures(v)
    check_zfight(v)
    return v.ok


def validate_files(m, shape, frame_shape, rig, body_shape, stages):
    ok = True
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    if not used <= set(shape["textures"]):
        print(f"FAIL texture codes not declared: {sorted(used - set(shape['textures']))}")
        ok = False
    names = {e["name"] for e in shape["elements"]}
    frame = {e["name"] for e in frame_shape["elements"]}
    if frame != {n for n in names if part_of(rig["parts"], n) == "frame"}:
        print("FAIL the frame shape is not the frame part")
        ok = False
    # the written body, flattened as the game reads it, is still the hung pose (rounding included)
    rig_k, mats = m.hung_matrices(body_shape)
    worst = 0.0
    for w in flatten(shape["elements"], textures={}):
        if not w.name.startswith("b_"):
            continue
        name = w.name.split("_", 2)[2]
        want = m.body_corners(rig_k, mats, name)
        worst = max(worst, max(min(math.dist(p, q) for q in w.corners()) for p in want))
    print(f"written body against kin's hung pose: worst corner {worst:.2e} voxels")
    if worst > TOL:
        print("FAIL the written body is not the hung pose")
        ok = False
    hollow = sum(1 for c in rig["cells"] if c.get("hollow"))
    print(f"cells: {len(rig['cells'])}, {hollow} hollow")
    if [0, 0, 0] not in [c["pos"] for c in rig["cells"]] or rig["cells"][0].get("hollow"):
        print("FAIL the controller cell is not solid")
        ok = False
    return ok
