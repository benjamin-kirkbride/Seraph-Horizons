"""The eidolon gantry generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model
breaks its rule. Everything is in the build frame (voxels), which is also the shipped frame divided
by 16 (the controller cell is the build frame's corner cell).
"""

from __future__ import annotations

import math
import re

import kin
from machinegen.checks import coplanar_faces, euler_round_trip, frame_floating, touching
from machinegen.geometry import El, flatten, rotate
from machinegen.rigmath import part_of, posed

TOL = 0.01                                    # voxels: the baked body against kin's hung pose, the floor
GANTRY = ("frame", "winch", "sheave", "lead", "fall", "hook", "ring")   # made for the gantry (the spine is vanilla's)
FIXED = ("frame", "winch", "sheave", "lead", "fall", "hook")            # what nothing hung may touch
CRANK = ("wn_crank", "wn_handle", "wn_collar")                          # the crank, outside the frame


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
    """The baked body and spine are the hung pose: every element's corners where kin (the game's maths) puts them."""
    rig, mats = v.m.hung_matrices(body_shape)
    worst = 0.0
    for code in [*v.m.body_codes(v.m.load_body()[1]), "spine"]:
        for el in v.by_part.get(code, []):
            name = v.m.source_name(el.name)
            want = v.m.body_corners(rig, mats, name)
            got = el.corners()
            worst = max(worst, max(min(math.dist(p, q) for q in got) for p in want))
    print(f"baked body against kin's hung pose: worst corner {worst:.2e} voxels")
    if worst > TOL:
        v.fail("the baked body is not the hung pose")


def check_stages(v, body_shape, stages):
    """Every drawn element of the eidolon is in the gantry once, in its stage's part, and the spine's in the
    spine part; the spine and the ring over its top peg are there from the start, needing nothing; after every
    stage what hangs so far, the spine and the body, is one piece (the eidolon's own rule, with the spine
    there from the first: the torso, the first stage, is clamped to it, and comes before the pelvis it hangs
    from in the shape's hierarchy, which the baked pose makes harmless; nothing may float); and the torso is
    clamped to the spine where they touch."""
    st = v.m.stage_of(stages)
    spine = v.m.spine_names()
    rig = kin.Rig(v.m.with_spine(body_shape))
    drawn = [n for n in rig.order if v.m.drawn(rig.elements[n])]
    faceless = sorted(n for n in rig.order if not v.m.drawn(rig.elements[n]))
    missing = [n for n in rig.order if n not in st and n not in spine]
    if missing:
        v.fail(f"eidolon elements in no stage: {missing[:6]}")
    if spine & set(st):
        v.fail(f"spine elements in a build stage: {sorted(spine & set(st))[:6]}")
    have = {el.name for code in [*v.m.body_codes(stages), "spine"] for el in v.by_part.get(code, [])}
    want = {v.m.baked_name(n, st) for n in drawn}
    if have != want:
        v.fail(f"stage coverage: missing {sorted(want - have)[:6]}, extra {sorted(have - want)[:6]}")
    shown = set(spine)
    for s in stages["stages"]:
        shown |= set(s["elements"])
        tops = [n for n in rig.order if n in shown and rig.parent[n] not in shown]
        if len(tops) > 1:
            v.fail(f"after {s['code']} the spine and body are in {len(tops)} pieces, from {tops[:6]}")
    if "spine-hook1" not in spine:
        v.fail("the spine's peg (spine-hook1) is not the spine's")
    for pid in ("ring", "spine"):
        part = next(p for p in v.parts if p["id"] == pid)
        if part["requires"] is not None or part["ride"] != "hook":
            v.fail(f"the {pid} needs {part['requires']!r} and rides {part['ride']!r}: it is the gantry's, there from the start, on the hook")
    first = v.m.body_codes(stages)[0]
    clamps = touching(v.posed([first], 0.0), v.posed(["spine"], 0.0))
    print(f"the {first} on the spine: {len(clamps)} contacts, the spine's {sorted({b for _, b in clamps})}")
    if not clamps:
        v.fail(f"the first stage, {first}, does not touch the spine it is clamped to")
    counts = {code: len(v.by_part.get(code, [])) for code in [*v.m.body_codes(stages), "spine"]}
    print(f"stages: {counts}; {len(have)} of {len(rig.order)} eidolon and spine elements (left out, no drawn face: {faceless})")


def check_floor(v, stages):
    body = list(v.m.body_codes(stages))
    for depth, want in ((0.0, v.m.drop()), (1.0, 0.0)):
        low = v.m.lowest(v.posed(body, depth))
        print(f"floor: the body's lowest point at depth {depth}: {low:.3f} (want {want:.3f})")
        if abs(low - want) > TOL:
            v.fail(f"the body's lowest point at depth {depth} is {low:.3f}, not {want:.3f}")
    if abs(v.m.drop() - 3.0) > TOL:
        v.fail(f"the hung toe is {v.m.drop():.3f} above the floor, not the eidolon's 3 (HUNG_CLEAR)")


def overhang(m, lo, hi):
    """How far a box pokes out of the model's place: the machine box, or the crank's column (regions())."""
    return min(max(max(r[0][k] - lo[k], hi[k] - r[1][k]) for k in range(3)) for r in m.regions())


def check_containment(v):
    worst, where = -1e9, None
    for depth in (0.0, 0.5, 1.0):
        for el in v.posed(list(v.by_part), depth):
            lo, hi = el.aabb()
            o = overhang(v.m, lo, hi)
            if o > worst:
                worst, where = o, el.name
    print(f"containment: worst overhang {worst:.2f} voxels ({where})")
    if worst > 0.01:
        v.fail(f"{where} pokes {worst:.2f} voxels out of the machine box and the crank's cell")
    outside = sorted(el.name for el in v.els if el.aabb()[1][2] > v.m.CELLS_Z * v.m.B + 0.01)
    print(f"outside the frame, in the crank's cell: {outside}")


def check_crank(v):
    """The crank is outside the frame, where a player can work it: a full turn of the winch (the crank, the
    collar, the drum and its hoops; the axle runs in its bearings) clears the frame, and the crank and its
    handle stay south of the frame's outer face, in the crank's cell."""
    m = v.m
    zc = m.BODY_AT[2]
    pivot = (m.DRUM[0], m.DRUM[1], zc)
    winch = [el for el in v.by_part["winch"] if not el.name.startswith("wn_axle")]
    frame = v.by_part["frame"]
    cx, cy, cz = m.CRANK_CELL
    cell = ((cx * m.B, cy * m.B, cz * m.B), ((cx + 1) * m.B, (cy + 1) * m.B, (cz + 1) * m.B))
    hits, out = set(), 0.0
    for deg in range(0, 360, 10):
        turned = [el.clone() for el in winch]
        rotate(turned, "z", float(deg), pivot)
        hits |= touching(turned, frame)
        for el in turned:
            if el.name.startswith(CRANK):
                lo, hi = el.aabb()
                out = max(out, max(max(cell[0][k] - lo[k], hi[k] - cell[1][k]) for k in range(3)))
    handle = next(el for el in v.by_part["winch"] if el.name == "wn_handle")
    print(f"crank: a full turn touches {len(hits)} frame elements; the crank out of its cell by {out:.2f}; "
          f"the handle at z {handle.aabb()[0][2]:.1f}..{handle.aabb()[1][2]:.1f} (the frame's outer face at {m.CELLS_Z * m.B:.0f})")
    if hits:
        v.fail(f"the winch, turned, touches the frame: {sorted(hits)[:6]}")
    if out > 0.01:
        v.fail("the crank leaves its cell as it turns")


def check_clearances(v, stages):
    """The body touches nothing of the gantry but the spine it is clamped to, and the spine nothing but the
    ring's bottom bar, under its peg."""
    body = list(v.m.body_codes(stages))
    for depth in (0.0, 0.5, 1.0):
        fixed = v.posed(list(FIXED), depth)
        hits = touching(v.posed(body, depth), fixed)
        spine = touching(v.posed(["spine"], depth), fixed)
        ring = touching(v.posed(body, depth), v.posed(["ring"], depth))
        ring |= {h for h in touching(v.posed(["spine"], depth), v.posed(["ring"], depth))
                 if not (h[0] == "sp_spine-hook1" and h[1] == "rg_bottom")}
        print(f"clearances at depth {depth}: body-gantry {len(hits)}, spine-gantry {len(spine)}, ring (but on the peg) {len(ring)}")
        for what, h in (("the body touches the gantry", hits), ("the spine touches the gantry", spine),
                        ("the ring touches the body or spine", ring)):
            if h:
                v.fail(f"{what} at depth {depth}: {sorted(h)[:6]}")


def check_awakening(v, body_shape):
    """The eidolon's `activate`, played where it hung (the entity at BODY_AT, the gantry at depth 0): it
    touches nothing of the gantry but the spine it was clamped to, and steps clear of that, for good."""
    m = v.m
    rig = kin.Rig(body_shape)
    anim = next(a for a in body_shape["animations"] if a["code"] == "activate")
    fixed = v.posed([*FIXED, "ring"], 0.0)
    spine = v.posed(["spine"], 0.0)
    hits, last = set(), None
    for f in range(anim["quantityframes"]):
        body = standing(m, rig, kin.sample(anim, f))
        hits |= touching(body, fixed)
        if touching(body, spine):
            last = f
    print(f"awakening: touches the gantry {len(hits)} times; on the spine until frame {last}, clear after it")
    if hits:
        v.fail(f"the awakening runs into the gantry: {sorted(hits)[:6]}")
    if last is None or last >= AWAKE_CLEAR:
        v.fail(f"the awakening is still on the spine at frame {last} (it must be clear from frame {AWAKE_CLEAR})")


AWAKE_CLEAR = 56   # activate's frame by which the body is clear of the spine: its first step down


def standing(m, rig, pose):
    """The body (no spine) posed by kin and placed at the entity's position, as static boxes."""
    mats = rig.all_matrices(pose)
    out = []
    for n in rig.order:
        e = rig.elements[n]
        if not m.drawn(e):
            continue
        size = [e["to"][i] - e["from"][i] for i in range(3)]
        c = kin.apply(mats[n], [s / 2 for s in size])
        out.append(El(n, size, [c[k] + m.OFF[k] for k in range(3)], [list(r) for r in kin.rotation_of(mats[n])], {}, None))
    return out


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
    if not held(axle, ["fr_bearing_r", "fr_bearing_l", "fr_bearing_post"]) or not all(axle[0][2] < frame[c][1][2] and frame[c][0][2] < axle[1][2] for c in cheeks):
        v.fail("the winch's axle does not run in both cheeks and the pillow block")
    pb = frame["fr_bearing_post"]
    if not all(pb[0][k] < axle[0][k] and axle[1][k] < pb[1][k] for k in (0, 1)) or axle[1][2] <= v.m.CELLS_Z * v.m.B:
        v.fail("the axle does not pass through the pillow block and out of the frame to the crank")
    if not held(pin, ["fr_hanger1", "fr_hanger2"]):
        v.fail("the sheave's pin does not run in both hangers")


def check_exit(v, body_shape):
    """The front is open: the eidolon standing where it woke (vanilla's rest pose at the entity's position)
    walks straight out of the west side touching nothing of the frame, the winch, the hung chains or the
    empty spine, its whole height."""
    m = v.m
    rest = standing(m, kin.Rig(body_shape), {})
    gantry = v.posed([*FIXED, "ring", "spine"], 0.0)
    hits = set()
    step = 2.0
    shift = 0.0
    while shift <= m.BODY_AT[0] + 20.0:
        moved = []
        for el in rest:
            e2 = el.clone()
            e2.c = [el.c[0] - shift, el.c[1], el.c[2]]
            moved.append(e2)
        hits |= touching(moved, gantry)
        shift += step
    lo = min(min(c[2] for c in el.corners()) for el in rest)
    hi = max(max(c[2] for c in el.corners()) for el in rest)
    top = max(max(c[1] for c in el.corners()) for el in rest)
    print(f"exit: the standing body (z {lo:.1f}..{hi:.1f}, {top:.1f} tall) walks out west through the opening z "
          f"{m.Z_RIGHT[1]:.0f}..{m.Z_LEFT[0]:.0f} under the front beam at {m.HEAD[0]:.0f}: {len(hits)} contacts")
    if hits:
        v.fail(f"the way out is blocked: {sorted(hits)[:6]}")


def check_room(v, stages):
    """Room to walk round the hung body and spine and fit parts: the gaps between them and the posts, the winch."""
    m = v.m
    body = v.posed([*m.body_codes(stages), "spine"], 0.0)
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
    (r"^wn_(axle|hoop|crank|collar)", {"iron"}),
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
    check_crank(v)
    check_clearances(v, stages)
    check_awakening(v, body_shape)
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
        if not w.name.startswith(("b_", "sp_")):
            continue
        name = m.source_name(w.name)
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
