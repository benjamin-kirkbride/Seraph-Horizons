"""The eidolon gantry generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model
breaks its rule. Everything is in the build frame (voxels), which is also the shipped frame divided
by 16 (the controller cell is the build frame's corner cell).
"""

from __future__ import annotations

import math
import re

import kin
from machinegen.checks import coplanar_faces, euler_round_trip, frame_floating, obb_obb, touching
from machinegen.geometry import El, aabb_of, flatten, rotate
from machinegen.rigmath import part_of, posed

TOL = 0.01                                    # voxels: the baked body against kin's hung pose, the floor
# the train's three shafts, each the part that turns and the parts fitted on it in later stages, riding it
TRAIN = {"crank": ("crank", "cranklantern", "crankhoops", "ratchet", "crankarm"),
         "layshaft": ("layshaft", "laygears", "laystraps"),
         "drum": ("drumshaft", "drumwheel", "drum", "drumstraps", "coil")}
WINCH = (*(pid for pids in TRAIN.values() for pid in pids), "pawl", "pawlmount")   # the winch but the sheave and chains
GANTRY = ("frame", *WINCH, "sheave", "lead", "fall", "hook", "ring")    # made for the gantry (the spine is vanilla's)
FIXED = ("frame", *WINCH, "sheave", "lead", "fall", "hook")             # what nothing hung may touch
OUTSIDE = ("ck_web", "ck_handle", "ck_ratchet", "pw_", "pm_")           # the crank, its ratchet and pawl, outside the frame
# What each part is fitted onto: it may not come in an earlier stage than these (the same stage is fine). A gear
# needs its shaft, the drum its shaft, a band what it binds, the ratchet and the crank the crank shaft (the ratchet
# slid on before the crank's web), the pawl its pin, the chain the drum and the sheave, the spine the chain's ring,
# and the body the spine.
NEEDS = {"cranklantern": ("crank",), "laygears": ("layshaft",), "drumwheel": ("drumshaft",), "drum": ("drumshaft",),
         "crankhoops": ("cranklantern",), "laystraps": ("layshaft", "laygears"), "drumstraps": ("drumshaft", "drum"),
         "ratchet": ("crank",), "pawl": ("pawlmount", "ratchet"), "crankarm": ("crank", "ratchet"),
         "coil": ("drum",), "lead": ("drum", "sheave", "coil"), "fall": ("sheave", "lead"), "hook": ("fall",),
         "ring": ("hook",), "spine": ("ring",)}
MESH_LIMIT = 0.25                             # voxels: cogs and staves run into each other this far at most (box cogs, octagon staves)
ENGAGE = 0.4                                  # voxels: at every moment a stave of each lantern is this close to a cog of its wheel at most


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
    spine part; the ring over its top peg comes with the chain and the spine with its own stage; after every
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
    for pid, want in (("ring", "chain"), ("spine", v.m.SPINE_STAGE)):
        part = next(p for p in v.parts if p["id"] == pid)
        if part["requires"] != want or part["ride"] != "hook":
            v.fail(f"the {pid} needs {part['requires']!r} and rides {part['ride']!r}: it comes with {want!r}, on the hook")
    first = v.m.body_codes(stages)[0]
    clamps = touching(v.posed([first], 0.0), v.posed(["spine"], 0.0))
    print(f"the {first} on the spine: {len(clamps)} contacts, the spine's {sorted({b for _, b in clamps})}")
    if not clamps:
        v.fail(f"the first stage, {first}, does not touch the spine it is clamped to")
    counts = {code: len(v.by_part.get(code, [])) for code in [*v.m.body_codes(stages), "spine"]}
    print(f"stages: {counts}; {len(have)} of {len(rig.order)} eidolon and spine elements (left out, no drawn face: {faceless})")


def joined(a, b, la, lb, aligned_a, aligned_b):
    """Whether two elements hold each other: machinegen's frame_floating rule (a shared face between two
    axis-aligned boxes, else an overlap)."""
    ov = [min(la[1][k], lb[1][k]) - max(la[0][k], lb[0][k]) for k in range(3)]
    if min(ov) < -0.02:
        return False
    if aligned_a and aligned_b:
        return sum(o > 0.05 for o in ov) >= 2
    return obb_obb(a, b, eps=-0.03)


def stage_floating(groups, ground=0.01):
    """Each stage's elements (`groups`: [(stage, elements)], in build order) joined to the ground through what is
    already there and itself: {stage: the names of its elements that float}. An element of an earlier stage is
    never held up by a later one."""
    placed, boxes, aligned, seen, out = [], [], [], [], {}
    for stage, els in groups:
        new = list(range(len(placed), len(placed) + len(els)))
        for el in els:
            placed.append(el)
            boxes.append(el.aabb())
            aligned.append(all(el.local_axis_for(k) is not None for k in range(3)))
            seen.append(False)
        todo = []
        for j in new:
            if boxes[j][0][1] <= ground or any(seen[i] and joined(placed[i], placed[j], boxes[i], boxes[j], aligned[i], aligned[j])
                                               for i in range(new[0]) if seen[i]):
                seen[j] = True
                todo.append(j)
        while todo:
            i = todo.pop()
            for j in new:
                if not seen[j] and joined(placed[i], placed[j], boxes[i], boxes[j], aligned[i], aligned[j]):
                    seen[j] = True
                    todo.append(j)
        out[stage] = sorted(placed[j].name for j in new if not seen[j])
    return out


def check_build(v, stages):
    """The build: the frame needs nothing; every other part needs one value, the winch's stages in their order, then
    the spine, then the body's stages; nothing comes before what it is fitted onto (NEEDS); and every step can be
    built, nothing floating: at rest, the frame and each winch stage in turn joined to the ground through what is
    already there (a gear through its shaft, the drum through its shaft, the chain through the drum and the sheave),
    the spine hung on the ring and the torso clamped to the spine (check_stages)."""
    m = v.m
    order = m.build_order(stages)
    staged = m.stage_of_part()
    req = {p["id"]: p["requires"] for p in v.parts}
    bad = [(pid, r) for pid, r in req.items() if r != (None if pid == "frame" else staged.get(pid, pid))]
    if bad:
        v.fail(f"parts needing the wrong stage: {bad[:6]}")
    if sorted({r for r in req.values() if r}, key=order.index) != order:
        v.fail(f"stages with no part: {sorted(set(order) - set(req.values()))}")
    late = [(pid, need) for pid, needs in [*NEEDS.items(), (m.body_codes(stages)[0], ("spine",))] for need in needs
            if order.index(req[need]) > order.index(req[pid])]
    if late:
        v.fail(f"parts fitted before what they are fitted onto: {late}")
    groups = [("frame", v.posed(["frame"], 0.0))]
    for code, _, _, pids in m.winch_stages():
        groups.append((code, v.posed(list(pids), 0.0)))
    loose = {k: n for k, n in stage_floating(groups).items() if n}
    hung = {(a.name, b.name) for a in v.posed(["spine"], 0.0) for b in v.posed(["ring"], 0.0)
            if obb_obb(a, b, eps=-0.02)}            # resting on it: touching, not in it
    print("build: " + " > ".join(f"{code} ({len(els)})" for code, els in groups) + f" > spine on the ring {sorted(hung)}"
          + f" > {' > '.join(m.body_codes(stages))}; floating: {loose or 'nothing'}")
    if loose:
        v.fail(f"a stage of the build floats: {loose}")
    if not hung:
        v.fail("the spine does not hang on the ring")
    for code, item, count, pids in m.winch_stages():
        if count < 1 or not item.startswith("game:"):
            v.fail(f"stage {code} takes {count} of {item}")
    code, item, count, pids = m.spine_stage()
    mast = [el for el in v.by_part["spine"] if el.name[3:] in m.SPINE_TIMBERS]
    print(f"spine stage: {count} × {item}, the mast in {len(mast)} lengths")
    if (code, item, pids) != (m.SPINE_STAGE, "game:supportbeam-{wood}", ("spine",)) or count != len(mast) or count < 1:
        v.fail(f"the spine's stage takes {count} of {item}, its mast is {len(mast)} lengths")
    timbers = m.frame_timbers(v.by_part["frame"])
    straps = {re.sub(r"_\d+$", "", el.name) for pid in ("crankhoops", "laystraps", "drumstraps") for el in v.by_part[pid]}
    axles = sum(1 for pid in ("layshaft", "drumshaft") for el in v.by_part[pid] if re.search(r"_shaft\d+_1$", el.name))
    want = {c: n for c, _, n, _ in m.winch_stages()}
    print(f"build counts: {len(timbers)} timbers in the frame (a support beam each), {axles} axle lengths "
          f"(axles {want['axles']}), {len(straps)} iron bands (strapping {want['strapping']}), the chain "
          f"{m.chain_length() / m.B:.2f} blocks (chain {want['chain']})")
    if axles != want["axles"] or len(straps) != want["strapping"] or m.frame_recipe(v.by_part["frame"])["beams"][1] != len(timbers):
        v.fail("a stage's count is not what the model has")


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


def train(m):
    """The gear train: (shaft, axis (x, y), its turn per turn of the crank), the crank first."""
    k = m.PINION_TEETH / m.WHEEL_TEETH
    return (("crank", m.CRANK_AXIS, 1.0), ("layshaft", m.LAY, -k), ("drum", m.DRUM, k * k))


RUNS_IN_BEARINGS = re.compile(r"^(ck_shaft|\w\w_gudgeon)")   # what runs in the frame's bearings and pillow block


def turned_train(v, crank_deg, shafts=False):
    """The train's elements by shaft, turned as the crank turns `crank_deg` from rest (the crank shaft and the
    gudgeons left out unless asked: they run in the frame's bearings)."""
    m = v.m
    zc = m.BODY_AT[2]
    out = {}
    for shaft, c, share in train(m):
        els = [el.clone() for pid in TRAIN[shaft] for el in v.by_part[pid] if shafts or not RUNS_IN_BEARINGS.match(el.name)]
        rotate(els, "z", crank_deg * share, (c[0], c[1], zc))
        out[shaft] = els
    return out


def check_crank(v):
    """The crank is outside the frame, where a player can work it, and the train turns free: through a full
    turn of the crank (the layshaft and the drum turning by the tooth counts) no gear, axle, the drum, the ratchet or
    the crank touches the frame, the pawl's mount or the thrown-off pawl, no two shafts' parts touch but meshing
    teeth (the shafts and gudgeons included), and the crank, its ratchet and handle stay south of the frame's outer
    face, in the crank's cell."""
    m = v.m
    frame = v.by_part["frame"] + v.by_part["pawlmount"]
    pawl = v.posed(["pawl"], 1.0)
    cx, cy, cz = m.CRANK_CELL
    cell = ((cx * m.B, cy * m.B, cz * m.B), ((cx + 1) * m.B, (cy + 1) * m.B, (cz + 1) * m.B))
    hits, cross, out = set(), set(), 0.0
    for deg in range(0, 360, 5):
        t = turned_train(v, float(deg))
        every = [el for els in t.values() for el in els]
        hits |= touching(every, frame) | touching(every, pawl)
        t = turned_train(v, float(deg), shafts=True)
        cross |= {h for h in touching(t["crank"], t["drum"])}
        cross |= {h for a, b in (("crank", "layshaft"), ("layshaft", "drum"))
                  for h in touching(t[a], t[b]) if not meshing(*h)}
        for el in t["crank"]:
            if el.name.startswith(OUTSIDE):
                lo, hi = el.aabb()
                out = max(out, max(max(cell[0][k] - lo[k], hi[k] - cell[1][k]) for k in range(3)))
    handle = next(el for el in v.by_part["crankarm"] if el.name == "ck_handle")
    print(f"crank: a full turn touches {len(hits)} frame or pawl elements and {len(cross)} pairs across shafts but "
          f"meshing teeth; the crank out of its cell by {out:.2f}; the handle at z {handle.aabb()[0][2]:.1f}.."
          f"{handle.aabb()[1][2]:.1f} (the frame's outer face at {m.CELLS_Z * m.B:.0f})")
    if hits:
        v.fail(f"the winch, turned, touches the frame or the pawl: {sorted(hits)[:6]}")
    if cross:
        v.fail(f"the train's shafts touch each other but at the teeth: {sorted(cross)[:6]}")
    if out > 0.01:
        v.fail("the crank leaves its cell as it turns")
    inside = sorted(el.name for pid in (*TRAIN["crank"], "pawl", "pawlmount") for el in v.by_part[pid]
                    if el.name.startswith(OUTSIDE) and el.aabb()[0][2] < m.CELLS_Z * m.B - 0.01)
    if inside:
        v.fail(f"parts of the crank or the pawl are inside the frame: {inside}")


def meshing(a, b):
    """Whether two elements are a lantern's stave and a wheel's cog (they may touch as they mesh)."""
    return ("_stave" in a and "_cog" in b) or ("_cog" in a and "_stave" in b)


def point_box(p, el):
    """The distance from a point to a box (0 inside it)."""
    d = [p[k] - el.c[k] for k in range(3)]
    q = [sum(el.r[k][j] * d[k] for k in range(3)) for j in range(3)]       # into the box's own axes
    return math.sqrt(sum(max(abs(q[j]) - el.size[j] / 2, 0.0) ** 2 for j in range(3)))


def overlap_depth(a, b):
    """How far two boxes run into each other: the shrink at which obb_obb stops finding an overlap, twice."""
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


def check_gearing(v, rig):
    """The train: every lantern's and wheel's pitch radius is the module's for its staves or cogs, each stage's
    centre distance the sum of its pitch radii, the rig's turns the counts' (each shaft the other way to the one it
    meshes with, the crank RATIO times the drum, which pays out the drop), the staves among the cogs at rest (none
    in a cog), and through a full turn of the crank the staves engaged and in the cogs at most MESH_LIMIT."""
    m = v.m
    errs = [abs(m.R_PINION - m.MODULE * m.PINION_TEETH / 2), abs(m.R_WHEEL - m.MODULE * m.WHEEL_TEETH / 2),
            abs(math.dist(m.CRANK_AXIS, m.LAY) - m.MESH), abs(math.dist(m.LAY, m.DRUM) - m.MESH)]
    shafts = {TRAIN[k][0]: k for k in TRAIN}
    amount = {shafts[p["id"]]: next(d["amount"] for d in p["drivers"] if d["type"] == "step") for p in rig["parts"]
              if p["id"] in shafts}
    riders = [p["id"] for p in rig["parts"] if p["id"] in WINCH and p["id"] not in shafts and p["id"] not in ("pawl", "pawlmount")
              and (p["ride"] != next(TRAIN[k][0] for k in TRAIN if p["id"] in TRAIN[k]) or p["drivers"])]
    if riders:
        v.fail(f"parts fitted on a shaft that do not ride it alone: {riders}")
    k = m.WHEEL_TEETH / m.PINION_TEETH
    ratio_errs = [abs(amount["crank"] / amount["layshaft"] + k), abs(amount["layshaft"] / amount["drum"] + k),
                  abs(amount["crank"] / amount["drum"] - m.RATIO), abs(amount["drum"] * m.DRUM_R - m.drop())]
    print(f"gearing: {m.PINION_TEETH} staves to {m.WHEEL_TEETH} cogs twice, {m.RATIO:g} to 1, module {m.MODULE}; centre distances off "
          f"{max(errs):.1e}, rig turns off the tooth counts {max(ratio_errs):.1e}; the let-down turns the drum "
          f"{math.degrees(amount['drum']):.1f} degrees and the crank {amount['crank'] / (2 * math.pi):.2f} turns")
    if max(errs) > 1e-9:
        v.fail("a stage's centre distance is not the sum of its pitch radii")
    if max(ratio_errs) > 1e-4:
        v.fail("the rig's turns are not the tooth counts' (or the drum does not pay out the drop)")
    pairs = (("crank", "ck_lantern_stave", "layshaft", "ls_wheel_cog", m.LAY),
             ("layshaft", "ls_lantern_stave", "drum", "dr_wheel_cog", m.DRUM))
    rest = turned_train(v, 0.0)
    for pa, na, pb, nb, _ in pairs:
        a = [el for el in rest[pa] if el.name.startswith(na)]
        b = [el for el in rest[pb] if el.name.startswith(nb)]
        if touching(a, b):
            v.fail(f"{na} and {nb} are in each other at rest")
    # a full crank turn: six staves of the crank's lantern, and more than one stave pitch of the layshaft's (72 of 60
    # degrees). At every step the staves and cogs run into each other at most MESH_LIMIT, and the lantern is engaged:
    # a stave inside the wheel's tip circle within ENGAGE of a cog, so the wheel is always driven
    steps = 180
    tip = m.R_WHEEL + m.ADDENDUM
    worst, slack, inside = {}, {}, {}
    for i in range(steps):
        t = turned_train(v, 360.0 * i / steps)
        for pa, na, pb, nb, centre in pairs:
            a = [el for el in t[pa] if el.name.startswith(na)]
            b = [el for el in t[pb] if el.name.startswith(nb)]
            d = 0.0
            for x in a:
                xl, xh = x.aabb()
                for y in b:
                    yl, yh = y.aabb()
                    if all(xl[q] < yh[q] and yl[q] < xh[q] for q in range(3)):
                        d = max(d, overlap_depth(x, y))
            worst[na] = max(worst.get(na, 0.0), d)
            zm = sum(el.c[2] for el in b) / len(b)
            staves = [el.c[:2] + [zm] for el in a if el.name.endswith("_1")]
            engaged = [s for s in staves if math.dist(s[:2], centre) < tip]
            gap = min((min(point_box(s, y) for y in b) - m.STAVE_R for s in engaged), default=math.inf)
            slack[na] = max(slack.get(na, 0.0), gap)
            inside[na] = min(inside.get(na, len(staves)), len(engaged))
    print("staves: " + ", ".join(f"{n} into its wheel's cogs at most {worst[n]:.3f}, at least {inside[n]} inside the cogs' tips, "
                                 f"the nearest at most {slack[n]:.3f} from a cog" for n in worst)
          + f" (limits {MESH_LIMIT}, {ENGAGE})")
    if max(worst.values()) > MESH_LIMIT:
        v.fail("a lantern's staves run too far into its wheel's cogs")
    if min(inside.values()) < 1 or max(slack.values()) > ENGAGE:
        v.fail("a lantern's staves leave its wheel's cogs: the wheel is not always driven")


def check_ratchet(v):
    """The pawl holds the load: at hung its nose is down in a gap of the ratchet, between root and tip, clear of
    the tooth behind it by PAWL_GAP, and the ratchet turned the let-down way by a little more runs into it; it is
    thrown off as the let-down starts without the ratchet touching it, and stays clear to the floor."""
    m = v.m
    nose, _, _, _ = m.pawl_geometry()
    r = math.dist(nose, m.CRANK_AXIS)
    ratchet = v.by_part["ratchet"]
    zc = m.BODY_AT[2]
    pivot = (m.CRANK_AXIS[0], m.CRANK_AXIS[1], zc)
    if not m.RATCHET_R[0] < r < m.RATCHET_R[1]:
        v.fail(f"the pawl's nose is at radius {r:.2f}, not between the ratchet's root and tip")
    turned = [el.clone() for el in ratchet]
    rotate(turned, "z", math.degrees((m.PAWL_GAP + 0.2) / m.RATCHET_R[0]), pivot)
    blocks = bool(touching(turned, v.posed(["pawl"], 0.0)))
    depths = [i * m.PAWL_LIFT_TO / 20 for i in range(41)] + [0.25, 0.5, 0.75, 1.0]
    hits = set()
    for dpt in depths:
        hits |= touching(v.posed(list(TRAIN["crank"]), dpt), v.posed(["pawl"], dpt))
    print(f"ratchet: {m.RATCHET_TEETH} teeth, the pawl's nose at radius {r:.2f} (root {m.RATCHET_R[0]}, tip {m.RATCHET_R[1]}); "
          f"turned the let-down way it {'runs into' if blocks else 'misses'} the pawl; thrown off over depth 0..{m.PAWL_LIFT_TO}: "
          f"{len(hits)} contacts")
    if not blocks:
        v.fail("the pawl does not stop the ratchet turning the let-down way")
    if hits:
        v.fail(f"the ratchet runs into the pawl as the winch lets down: {sorted(hits)[:6]}")


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
    """Nothing floats: the frame is one piece from the ground; the layshaft's and the drum's shafts run on their
    gudgeons in both cheeks' bearings, and the crank shaft in the left's and the pillow block, out to the crank;
    the wooden shafts reach their gudgeons; the pawl is on its pin; and the sheave's pin is in both hangers."""
    seen, loose = frame_floating(v.by_part["frame"])
    print(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")
    frame = {el.name: el.aabb() for el in v.by_part["frame"]}

    def held(shaft, holders):
        lo, hi = shaft
        return all(lo[2] < frame[h][0][2] and frame[h][1][2] < hi[2] for h in holders)
    cheeks = [n for n in frame if n.startswith("fr_cheek_")]

    def shaft(pid, prefix):
        return [el.aabb() for el in v.by_part[pid] if el.name.startswith(prefix)][0]
    def gudgeons(pid, prefix):
        return aabb_of([el for el in v.by_part[pid] if el.name.startswith(prefix)])
    crank, lay, axle = shaft("crank", "ck_shaft"), gudgeons("layshaft", "ls_gudgeon"), gudgeons("drumshaft", "dr_gudgeon")
    pin = aabb_of([el for el in v.by_part["frame"] if el.name.startswith("fr_iron_sheavepin")])
    left = [c for c in cheeks if c.startswith("fr_cheek_l")]
    for name, s, holders, through in (("crank shaft", crank, ["fr_bearing_ckl", "fr_bearing_post"], left),
                                      ("layshaft", lay, ["fr_bearing_lsr", "fr_bearing_lsl"], cheeks),
                                      ("drum's shaft", axle, ["fr_bearing_drr", "fr_bearing_drl"], cheeks)):
        if not held(s, holders) or not all(s[0][2] < frame[c][1][2] and frame[c][0][2] < s[1][2] for c in through):
            v.fail(f"the {name} does not run in its bearings ({', '.join(holders)}) and through its cheeks")
    for pid, pre in (("layshaft", "ls_"), ("drumshaft", "dr_")):
        wood = aabb_of([el for el in v.by_part[pid] if el.name.startswith(pre + "shaft")])
        for g in ("1", "2"):
            gl, gh = aabb_of([el for el in v.by_part[pid] if el.name.startswith(f"{pre}gudgeon{g}_")])
            if not (gl[2] < wood[1][2] and wood[0][2] < gh[2]):
                v.fail(f"the {pid}'s gudgeon {g} is not driven into its shaft")
    pb = frame["fr_bearing_post"]
    if not all(pb[0][k] < crank[0][k] and crank[1][k] < pb[1][k] for k in (0, 1)) or crank[1][2] <= v.m.CELLS_Z * v.m.B:
        v.fail("the crank shaft does not pass through the pillow block and out of the frame to the crank")
    pp = aabb_of([el for el in v.by_part["pawlmount"] if el.name.startswith("pm_pin")])
    pawl = v.by_part["pawl"][0].aabb()
    if not (pp[0][2] < pawl[0][2] and pawl[1][2] < pp[1][2]):
        v.fail("the pawl is not on its pin")
    if not held(pin, ["fr_hanger1", "fr_hanger2"]):
        v.fail("the sheave's pin does not run in both hangers")
    hub = aabb_of([el for el in v.by_part["sheave"] if el.name.startswith("sv_hub")])
    if not (pin[0][2] < hub[0][2] and hub[1][2] < pin[1][2]):
        v.fail("the sheave does not turn on its pin")


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


WOOD = {"wood", "wood-end"}                    # the gantry's wood-variant codes (make_shape.WOOD_CODES)
ROLES = [
    (r"^fr_(post|head|beam|hoist|sill|rail|knee|cheek)", WOOD),
    (r"^fr_(iron|bearing|hanger)", {"iron"}),
    (r"^ck_(shaft|ratchet|web)|^pw_|^pm_|^\w\w_(lantern_hoop|gudgeon|collar)|^dr_hoop", {"iron"}),
    (r"^\w\w_(lantern_(disc|stave)|wheel_|shaft\d)", {"mechanics"}),   # vanilla's axles and spur gears, as the game draws them
    (r"^ck_handle|^dr_drum", WOOD),
    (r"^dr_coil|^ld_|^fl_", {"chain"}),
    (r"^hk_|^rg_", {"iron"}),
    (r"^sv_(hub|flange)", WOOD),
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
    if set(v.m.WOOD_CODES) != WOOD or not WOOD <= set(v.m.GANTRY_TEXTURES) or v.m.MECHANICS not in v.m.GANTRY_TEXTURES:
        bad.append(("wood codes", sorted(v.m.WOOD_CODES), sorted(WOOD)))
    for el in v.by_part.get("spine", []):
        tex = {f["texture"].lstrip("#") for f in el.faces.values()}
        wooden = el.name[3:] in v.m.SPINE_WOODEN
        if wooden and not tex <= WOOD or not wooden and tex & WOOD:
            bad.append((el.name, sorted(tex), sorted(WOOD) if wooden else ["not wood"]))
    ends = sum(1 for el in v.els if el.part in GANTRY for f in el.faces.values() if f["texture"] == "#wood-end")
    print(f"textures by role: {sum(el.part in GANTRY for el in v.els) - len(bad)} gantry elements as their role says, "
          f"{ends} end-grain faces")
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
    check_build(v, stages)
    check_floor(v, stages)
    check_containment(v)
    check_crank(v)
    check_gearing(v, rig)
    check_ratchet(v)
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
    crank = next((c for c in rig["cells"] if c["pos"] == list(m.CRANK_CELL)), None)
    if crank is None or not crank.get("hollow") or "boxes" in crank:
        print("FAIL the crank's cell is not in the footprint, hollow, with no boxes")
        ok = False
    return ok
