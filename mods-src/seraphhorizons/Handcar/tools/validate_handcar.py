"""The handcar generator's checks: every one returns problems as strings, and `validate` runs them all.

`fix` runs first and changes the elements: the faces pressed against their own part's elements go,
and the smaller of each pair of coplanar faces is moved in (`machinegen.checks.fix_coplanar`).
"""

from __future__ import annotations

import math

from machinegen.checks import coplanar_faces, euler_round_trip, fix_coplanar, frame_floating, supports, touching
from machinegen.geometry import El, mvec
from machinegen.rigmath import part_matrix, part_of, posed

import riders

TAU = 2 * math.pi


def _helpers(el):
    return not el.faces


def pose_all(els, parts, theta):
    return [posed(el, part_matrix(parts, part_of(parts, el.name), {"theta": theta})) for el in els]


def cycle_thetas(M, n):
    return [TAU * M.RATIO * i / n for i in range(n)]


def fix(els, parts, M):
    drawn = [el for el in els if not _helpers(el)]
    poses = [0.0, TAU * M.RATIO * 0.25, TAU * M.RATIO * 0.5, TAU * M.RATIO * 0.75]
    before, hidden = fix_coplanar(drawn, lambda es, th: pose_all(es, parts, th), poses)
    print(f"z-fighting: {len(before[0.0])} coplanar pairs at rest before the fix; {hidden} hidden faces removed")


def check_parts(els, parts):
    problems = []
    counts = {}
    for el in els:
        pid = part_of(parts, el.name)
        counts[pid] = counts.get(pid, 0) + 1
        if pid != el.part:
            problems.append(f"parts: {el.name} was built for {el.part} but the rig puts it in {pid}")
    names = [el.name for el in els]
    if len(set(names)) != len(names):
        problems.append("parts: duplicate element names")
    for p in parts:
        if p["id"] not in counts:
            problems.append(f"parts: {p['id']} has no elements")
    print("elements per part:", ", ".join(f"{k} {v}" for k, v in sorted(counts.items())))
    if euler_round_trip(els) > 1e-6:
        problems.append("parts: an element's rotation does not survive Euler angles")
    return problems


def _apply_blocks(m, p_vox):
    pb = [v / 16 for v in p_vox]
    return [(m[i][0] * pb[0] + m[i][1] * pb[1] + m[i][2] * pb[2] + m[i][3]) * 16 for i in range(3)]


def check_linkage(parts, M, n=144):
    """The pitman's ends stay on their pins: the crank pin below, the beam's pin above."""
    problems = []
    p0 = (*M.crank_pin(0.0), M.MID_Z)
    q0 = (*M.beam_pin(0.0), M.MID_Z)
    worst_low = worst_high = 0.0
    for th in [TAU * M.RATIO * i / n for i in range(n)]:
        rod = part_matrix(parts, "pitman", {"theta": th})
        gear = part_matrix(parts, "gear", {"theta": th})
        beam = part_matrix(parts, "beam", {"theta": th})
        worst_low = max(worst_low, math.dist(_apply_blocks(rod, p0), _apply_blocks(gear, p0)))
        worst_high = max(worst_high, math.dist(_apply_blocks(rod, q0), _apply_blocks(beam, q0)))
    print(f"linkage: pitman's low pin off the crank pin by at most {worst_low:.4f} voxels, high pin off the beam's by {worst_high:.4f}")
    if worst_low > 0.05 or worst_high > 0.05:
        problems.append(f"linkage: the pitman leaves its pins (low {worst_low:.3f}, high {worst_high:.3f} voxels)")
    beta = [M.series(M.BETA_SERIES, -th / M.RATIO) for th in [TAU * M.RATIO * i / n for i in range(n)]]
    swing = (max(beta) - min(beta)) * 180 / math.pi
    travel = M.ARM * (math.sin(max(beta)) - math.sin(min(beta)))
    print(f"linkage: the beam swings {swing:.1f} degrees, the handles {travel:.2f} voxels up and down")
    if not 25 <= swing <= 50:
        problems.append(f"linkage: the beam's swing {swing:.1f} degrees is outside 25..50")
    return problems


def check_gearing(parts, M):
    problems = []
    centre = math.dist((M.X_FRONT, M.AXLE_Y), M.COUNTER)
    if abs(centre - (M.R_PINION + M.R_GEAR)) > 1e-9:
        problems.append(f"gearing: the pinion and gear centres are {centre:.4f} apart, not {M.R_PINION + M.R_GEAR}")
    rp = next(d for d in next(p for p in parts if p["id"] == "axle_front")["drivers"])["ratio"]
    rg = next(d for d in next(p for p in parts if p["id"] == "gear")["drivers"])["ratio"]
    if abs(rp * M.R_PINION + rg * M.R_GEAR) > 1e-6:
        problems.append(f"gearing: the pitch circles do not roll together ({rp} x {M.R_PINION} against {rg} x {M.R_GEAR})")
    if abs(M.AXLE_Y - M.R_WHEEL) > 1e-9:
        problems.append("wheels: the treads do not stand on the rails' top (y 0)")
    print(f"gearing: {M.PINION_TEETH} to {M.GEAR_TEETH} teeth, {M.RATIO:g} axle turns per stroke; one stroke is "
          f"{TAU * M.RATIO * M.R_WHEEL / 16:.4f} blocks of travel")
    return problems


# Pairs of name prefixes whose elements are meant to touch: shafts in bearings, pins in eyes, the gears meshing.
INTENDED = [
    ("gr_shaft", "fr_hanger"), ("fr_pivotpin", "bm_"), ("bm_pin", "pm_eye_high"), ("bm_pin", "pm_rod"),
    ("gr_crankpin", "pm_eye_low"), ("gr_crankpin", "pm_rod"), ("af_pinion", "gr_gear"), ("gr_crankboss", "pm_"),
    ("TNL_", "fr_quadrant"),
]


def _intended(a, b):
    return any((a.startswith(x) and b.startswith(y)) or (a.startswith(y) and b.startswith(x)) for x, y in INTENDED)


def check_clearances(els, parts, M, n=36):
    problems = []
    drawn = [el for el in els if not _helpers(el)]
    found = set()
    for th in cycle_thetas(M, n):
        ps = pose_all(drawn, parts, th)
        groups = {}
        for el, p in zip(drawn, ps):
            groups.setdefault(part_of(parts, el.name), []).append(p)
        ids = sorted(groups)
        for i, a in enumerate(ids):
            for b in ids[i + 1:]:
                if a.startswith("lever_") and b.startswith("lever_"):
                    continue                 # the three positions of one lever: only one is ever drawn
                for x, y in touching(groups[a], groups[b]):
                    if not _intended(x, y):
                        found.add(tuple(sorted((x, y))))
    for x, y in sorted(found)[:20]:
        problems.append(f"clearance: {x} touches {y}")
    if len(found) > 20:
        problems.append(f"clearance: and {len(found) - 20} more pairs")
    print(f"clearances: {n} poses over a stroke, {len(found)} unintended contacts")
    return problems


def check_frame(els, parts, M):
    problems = []
    frame = [el for el in els if part_of(parts, el.name) in ("frame", "lever_left", "lever_straight", "lever_right") and not _helpers(el)]
    seen, floating = frame_floating(frame, ground=M.SILL_Y[0] + 0.01)
    if floating:
        problems.append(f"frame: {len(floating)} elements float: {', '.join(floating[:8])}")
    shaft = [el for el in els if el.name.startswith("gr_shaft")]
    found, _ = supports(frame, shaft, 2, M.COUNTER)
    if len(found) < 2:
        problems.append(f"frame: the countershaft runs in {len(found)} bearings")
    pin = [el for el in els if el.name.startswith("fr_pivotpin")]
    found_pin, _ = supports([el for el in frame if el.name.endswith("_top")], pin, 2, M.PIVOT)
    if len(found_pin) < 2:
        problems.append(f"frame: the beam's pivot pin runs in {len(found_pin)} of the stand's bearings")
    print(f"frame: {len(frame)} elements joined to the sills; countershaft bearings {sorted(found)}; pivot bearings {sorted(found_pin)}")
    return problems


def check_coplanar(els, parts, M):
    drawn = [el for el in els if not _helpers(el)]
    problems = []
    for th in (0.0, 1.3, 4.0, 9.1):
        pairs = coplanar_faces(pose_all(drawn, parts, th))
        for a, da, b, db, area in pairs[:6]:
            problems.append(f"z-fighting at theta {th}: {a} {da} and {b} {db} ({area} sq voxels)")
    return problems


def check_textures(els, M):
    problems = []
    for el in els:
        for d, f in el.faces.items():
            if f["texture"].lstrip("#") not in M.TEXTURES:
                problems.append(f"textures: {el.name} {d} uses {f['texture']}, not declared")
    return problems


def check_cargo(els, parts, M):
    lo, hi = M.CARGO
    probe = El("cargo", [hi[k] - lo[k] - 0.05 for k in range(3)], [(lo[k] + hi[k]) / 2 for k in range(3)],
               [[1.0, 0, 0], [0, 1.0, 0], [0, 0, 1.0]], {}, "cargo")
    hits = set()
    drawn = [el for el in els if not _helpers(el)]
    for th in cycle_thetas(M, 24):
        for x, y in touching([probe], pose_all(drawn, parts, th)):
            hits.add(y)
    return [f"cargo: a chest's block on the cargo deck would meet {', '.join(sorted(hits))}"] if hits else []


# ---------------------------------------------------------------- the body's animation against the rig
def _local(e, deg=(0.0, 0.0, 0.0), off=(0.0, 0.0, 0.0)):
    return riders.local_matrix({"from": e["from"], "origin": e.get("rotationOrigin", [0.0, 0.0, 0.0]),
                                "rot": [e.get("rotationX", 0.0), e.get("rotationY", 0.0), e.get("rotationZ", 0.0)]}, deg, off)


def tree_centres(roots, values):
    """Every element's centre (voxels) with the joints posed by `values` {name: (deg, offset)}."""
    out = {}

    def walk(es, parent):
        for e in es:
            deg, off = values.get(e["name"], ((0.0, 0.0, 0.0), (0.0, 0.0, 0.0)))
            m = riders._mul(parent, _local(e, deg, off))
            half = [(e["to"][k] - e["from"][k]) / 2 for k in range(3)]
            out[e["name"]] = riders._apply(m, half)
            walk(e.get("children", []), m)
    walk(roots, [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]])
    return out


def anim_values(anim, f):
    """The joints' values at fractional frame f, as the game lerps keyframes (shortest way where
    the keyframe says so)."""
    n = anim["quantityframes"]
    i = int(math.floor(f)) % n
    t = f - math.floor(f)
    a, b = anim["keyframes"][i]["elements"], anim["keyframes"][(i + 1) % n]["elements"]
    out = {}
    for name, va in a.items():
        vb = b[name]
        deg = []
        for axis in "XYZ":
            x0, x1 = va.get(f"rotation{axis}", 0.0), vb.get(f"rotation{axis}", 0.0)
            if va.get(f"rotShortestDistance{axis}"):
                dd = (x1 - x0 + 180.0) % 360.0 - 180.0
                deg.append(x0 + dd * t)
            else:
                deg.append(x0 + (x1 - x0) * t)
        off = [va.get(f"offset{a_}", 0.0) + (vb.get(f"offset{a_}", 0.0) - va.get(f"offset{a_}", 0.0)) * t for a_ in "XYZ"]
        out[name] = (tuple(deg), tuple(off))
    return out


def check_body_animation(els, parts, body, M):
    problems = []
    anim = next(a for a in body["animations"] if a["code"] == M.BODY_ANIM)
    worst_key = worst_mid = 0.0
    by_name = {el.name: el for el in els}
    for f in [i * 0.5 for i in range(2 * M.RIDER_FRAMES)]:
        centres = tree_centres(body["elements"], anim_values(anim, f))
        th = M.theta_of_frame(f)
        for name, el in by_name.items():
            if name not in centres:
                continue
            p = posed(el, part_matrix(parts, part_of(parts, name), {"theta": th}))
            d = math.dist(p.c, centres[name])
            if f == int(f):
                worst_key = max(worst_key, d)
            else:
                worst_mid = max(worst_mid, d)
    print(f"body animation: elements off the rig by at most {worst_key:.4f} voxels at keyframes, {worst_mid:.4f} between them")
    if worst_key > 0.01:
        problems.append(f"body animation: the keyframes miss the rig by {worst_key:.4f} voxels")
    if worst_mid > 0.15:
        problems.append(f"body animation: between keyframes the parts are {worst_mid:.3f} voxels off the rig")
    return problems


# ---------------------------------------------------------------- the riders
def seraph_boxes(skel, pose, names):
    """The seraph's elements `names` as boxes (El) in the rider's model frame."""
    out = []
    for n in names:
        m = riders.world_matrix(skel, n, pose)
        e = skel[n]
        size = [e["to"][k] - e["from"][k] for k in range(3)]
        c = riders._apply(m, [s / 2 for s in size])
        r = [[m[i][j] for j in range(3)] for i in range(3)]
        out.append(El(n, size, c, r, {}, "rider"))
    return out


def to_car(M, seat_key, el):
    s = M.SEATS[seat_key]
    d = [el.c[0] - 8.0, el.c[1], el.c[2] - 8.0]
    r = [row[:] for row in el.r]
    if s["turn"] == 180.0:
        flip = [[-1, 0, 0], [0, 1, 0], [0, 0, -1]]
        d = mvec(flip, d)
        r = [[sum(flip[i][k] * r[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
    return El(el.name, el.size, [d[k] + s["pos"][k] for k in range(3)], r, {}, "rider")


BODY_PARTS = ("LowerTorso", "UpperTorso", "Head", "UpperFootR", "LowerFootR", "UpperFootL", "LowerFootL", "UpperArmR", "UpperArmL")


def check_riders(els, parts, skel, anims, M):
    problems = []
    samples = [i * 0.25 for i in range(4 * M.RIDER_FRAMES)]
    for key in M.SEATS:
        for anim in anims[key]:
            err = riders.worst_hand_error(skel, anim, lambda f, k=key: M.grip_targets(parts, k, f), samples)
            print(f"riders: {anim['code']}: hands off the handle by at most {err:.4f} voxels over the stroke")
            if err > 0.12:
                problems.append(f"riders: {anim['code']} puts a hand {err:.3f} voxels off the handle")
    # the riders' bodies (not their hands and forearms, which hold the handle) clear the car
    drawn = [el for el in els if not _helpers(el)]
    hits = set()
    for f in range(0, M.RIDER_FRAMES, 3):
        cars = pose_all(drawn, parts, M.theta_of_frame(f))
        for key in M.SEATS:
            for anim in anims[key]:
                pose = riders.pose_from_json(anim, f)
                bodies = [to_car(M, key, b) for b in seraph_boxes(skel, pose, BODY_PARTS)]
                for x, y in touching(bodies, cars, eps=0.05):
                    if x.startswith("LowerFoot") and y.startswith("fr_board"):
                        continue             # standing on the deck: the seraph's soles sink a little into what it stands on
                    hits.add((key, x, y))
    for key, x, y in sorted(hits)[:12]:
        problems.append(f"riders: the {key} rider's {x} meets {y}")
    if abs(M.SEATS["front"]["pos"][1] - M.DECK_Y[1]) > 1e-9 or abs(M.SEATS["rear"]["pos"][1] - M.DECK_Y[1]) > 1e-9:
        problems.append("riders: a seat is not on the deck")
    return problems


def validate(els, parts, skel, anims, body, M):
    problems = []
    problems += check_parts(els, parts)
    problems += check_textures(els, M)
    problems += check_linkage(parts, M)
    problems += check_gearing(parts, M)
    problems += check_clearances(els, parts, M)
    problems += check_frame(els, parts, M)
    problems += check_coplanar(els, parts, M)
    problems += check_cargo(els, parts, M)
    problems += check_body_animation(els, parts, body, M)
    problems += check_riders(els, parts, skel, anims, M)
    return problems
