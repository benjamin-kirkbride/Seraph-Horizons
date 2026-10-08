"""Validation helpers shared by the machines' generators.

Moved verbatim from the bucking sawmill's generator (its `validate` composes them, as every
machine's does); a value the mill read from a global is now an argument whose default is the
mill's. Every function is pure apart from `inset_face` and `fix_coplanar`, which change elements.
"""

from __future__ import annotations

import math

from .geometry import TRANSPARENT, El, euler_xyz, from_euler, mvec, scale_uv

CELL = 16.0                                  # voxels per block


# ---------------------------------------------------------------- separating-axis tests
def obb_overlap(el: El, lo, hi, eps=0.02):
    """Separating-axis test between a rotated element and an axis-aligned box (voxels)."""
    bc = [(lo[k] + hi[k]) / 2 for k in range(3)]
    bh = [(hi[k] - lo[k]) / 2 - eps for k in range(3)]
    eh = [max(abs(s) / 2 - eps, 0.0) for s in el.size]
    ea = [[el.r[i][k] for i in range(3)] for k in range(3)]  # element axes (columns)
    ba = [[1, 0, 0], [0, 1, 0], [0, 0, 1]]
    d = [el.c[k] - bc[k] for k in range(3)]
    axes = ea + ba
    for u in ea:
        for v in ba:
            w = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
            if sum(x * x for x in w) > 1e-9:
                axes.append(w)
    for ax in axes:
        dist = abs(sum(d[k] * ax[k] for k in range(3)))
        ra = sum(eh[k] * abs(sum(ea[k][i] * ax[i] for i in range(3))) for k in range(3))
        rb = sum(bh[k] * abs(ax[k]) for k in range(3))
        if dist > ra + rb:
            return False
    return True


def obb_obb(a: El, b: El, eps=0.02):
    """Separating-axis test between two rotated elements (voxels)."""
    ah = [max(abs(s) / 2 - eps, 0.0) for s in a.size]
    bh = [max(abs(s) / 2 - eps, 0.0) for s in b.size]
    aa = [[a.r[i][k] for i in range(3)] for k in range(3)]
    ba = [[b.r[i][k] for i in range(3)] for k in range(3)]
    d = [a.c[k] - b.c[k] for k in range(3)]
    axes = aa + ba
    for u in aa:
        for v in ba:
            w = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
            if sum(x * x for x in w) > 1e-9:
                axes.append(w)
    for ax in axes:
        dist = abs(sum(d[k] * ax[k] for k in range(3)))
        ra = sum(ah[k] * abs(sum(aa[k][i] * ax[i] for i in range(3))) for k in range(3))
        rb = sum(bh[k] * abs(sum(ba[k][i] * ax[i] for i in range(3))) for k in range(3))
        if dist > ra + rb:
            return False
    return True


def boxes_touch(a: El, b: El, eps=0.02):
    """Two elements overlap by more than `eps`: their bounding boxes first (cheap), then exactly."""
    alo, ahi = a.aabb()
    blo, bhi = b.aabb()
    return all(alo[k] < bhi[k] - eps and blo[k] < ahi[k] - eps for k in range(3)) and obb_obb(a, b)


def touching(ga, gb, eps=0.02):
    """Every (a name, b name) pair of elements of groups `ga` and `gb` that overlap (a clearance
    check: run it over posed groups, with intended contacts filtered out of the groups)."""
    hits = set()
    bb = [(e, e.aabb()) for e in gb]
    for e in ga:
        elo, ehi = e.aabb()
        for f, (flo, fhi) in bb:
            if all(elo[k] < fhi[k] - eps and flo[k] < ehi[k] - eps for k in range(3)) and obb_obb(e, f):
                hits.add((e.name, f.name))
    return hits


# ---------------------------------------------------------------- cells
def cells_touched(lo, hi, eps=0.01, size=CELL):
    """The cells (block coordinates) a box lo..hi (voxels) reaches into by more than `eps`."""
    return [(cx, cy, cz)
            for cx in range(int((lo[0] + eps) // size), int(math.ceil((hi[0] - eps) / size)))
            for cy in range(int((lo[1] + eps) // size), int(math.ceil((hi[1] - eps) / size)))
            for cz in range(int((lo[2] + eps) // size), int(math.ceil((hi[2] - eps) / size)))]


def box_overhang(lo, hi, cells, size=CELL):
    """How far (voxels) a box lo..hi pokes out of the machine box of `cells` (x, y, z counts) on
    its worst side; negative when it is inside."""
    return max(max(-lo[k], hi[k] - n * size) for k, n in enumerate(cells))


def cell_boxes(els, cell, size=CELL, max_boxes=3, min_gain=0.08):
    """Up to `max_boxes` cuboids (cell-local 0..1) covering the elements' AABBs clipped to the
    cell; a box is split only where that saves more than `min_gain` of the cell's volume."""
    lo_c = [cell[k] * size for k in range(3)]
    clipped = []
    for el in els:
        lo, hi = el.aabb()
        a = [max(lo[k], lo_c[k]) for k in range(3)]
        b = [min(hi[k], lo_c[k] + size) for k in range(3)]
        if all(b[k] - a[k] > 0.01 for k in range(3)):
            clipped.append((a, b))
    if not clipped:
        return None

    def bound(bs):
        return [min(x[0][k] for x in bs) for k in range(3)], [max(x[1][k] for x in bs) for k in range(3)]

    def vol(b):
        return math.prod(b[1][k] - b[0][k] for k in range(3))

    groups = [clipped]
    while len(groups) < max_boxes:
        best = None
        for gi, g in enumerate(groups):
            if len(g) < 2:
                continue
            whole = vol(bound(g))
            for axis in range(3):
                srt = sorted(g, key=lambda x: (x[0][axis] + x[1][axis]))
                for i in range(1, len(srt)):
                    a, b2 = srt[:i], srt[i:]
                    gain = whole - vol(bound(a)) - vol(bound(b2))
                    if best is None or gain > best[0]:
                        best = (gain, gi, a, b2)
        if best is None or best[0] < min_gain * size ** 3:
            break
        _, gi, a, b2 = best
        groups[gi:gi + 1] = [a, b2]
    out = []
    for g in groups:
        lo, hi = bound(g)
        out.append([round((lo[k] - lo_c[k]) / size, 4) for k in range(3)] + [round((hi[k] - lo_c[k]) / size, 4) for k in range(3)])
    return sorted(out)


LID = 1.0 / 16                                # blocks: a lid's thickness


def with_lids(cells, columns=None):
    """The rig's cells with a `lid` on the top cell of every column (or of every column (x, z) for
    which `columns(x, z)` is true, when given; the rest get none): the cell-local height of the top
    of a collision-only box over the whole cell, LID thick, so a player walking on the machine
    cannot drop between its boxes. Every lidded column whose top cell is in the same layer gets one
    height, the highest top of those cells (1 for a full cube or a hollow cell), so the top walks as
    one deck. New cell dicts, in the same order; a cell's own `boxes` and `hollow` are kept, and
    any `lid` it had is replaced."""
    top = {}
    for c in cells:
        x, y, z = c["pos"]
        if columns is None or columns(x, z):
            top[(x, z)] = max(top.get((x, z), y), y)

    def height(c):
        return max(b[4] for b in c["boxes"]) if c.get("boxes") else 1.0

    deck = {}
    for c in cells:
        x, y, z = c["pos"]
        if top.get((x, z)) == y:
            deck[y] = max(deck.get(y, 0.0), height(c))
    out = []
    for c in cells:
        x, y, z = c["pos"]
        c = {k: v for k, v in c.items() if k != "lid"}
        out.append({**c, "lid": deck[y]} if top.get((x, z)) == y else c)
    return out


def lid_gaps(cells, columns=None):
    """Columns (x, z) whose top cell has no lid of at least LID, or a lid above its cell; with
    `columns`, only those for which `columns(x, z)` is true, and any other column with a lid on
    any cell counts as a gap too (it should have none)."""
    top = {}
    for c in cells:
        x, y, z = c["pos"]
        if (x, z) not in top or y > top[(x, z)]["pos"][1]:
            top[(x, z)] = c
    gaps = set()
    for c in cells:
        x, y, z = c["pos"]
        if columns is not None and not columns(x, z) and "lid" in c:
            gaps.add((x, z))
    for col, c in top.items():
        if (columns is None or columns(*col)) and not (LID - 1e-9 <= c.get("lid", 0.0) <= 1.0):
            gaps.add(col)
    return sorted(gaps)


# ---------------------------------------------------------------- coplanar faces (z-fighting)
# A face's outward normal along the element's local axes.
FACE_NORMAL = {"east": (0, 1), "west": (0, -1), "up": (1, 1), "down": (1, -1), "south": (2, 1), "north": (2, -1)}
COPLANAR_EPS = 0.005                         # voxels: faces closer than this to one plane share it
COPLANAR_AREA = 0.01                         # square voxels: a smaller overlap does not show
COPLANAR_INSET = 0.015                       # voxels: how far a z-fighting face is moved in, per step


def drawn_faces(el: El):
    """The element's drawn faces in world voxels: (direction, unit normal, the 4 corners in order)."""
    out = []
    h = [abs(v) / 2 for v in el.size]
    for d, (k, sgn) in FACE_NORMAL.items():
        if d not in el.faces or el.faces[d].get("enabled") is False:
            continue
        u, v = [a for a in range(3) if a != k]
        quad = []
        for su, sv in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            local = [0.0, 0.0, 0.0]
            local[k], local[u], local[v] = sgn * h[k], su * h[u], sv * h[v]
            w = mvec(el.r, local)
            quad.append([el.c[i] + w[i] for i in range(3)])
        n = mvec(el.r, [1.0 if a == k else 0.0 for a in range(3)])
        out.append((d, [sgn * x for x in n], quad))
    return out


def _poly_area(poly):
    return abs(sum(poly[i][0] * poly[i - 1][1] - poly[i - 1][0] * poly[i][1] for i in range(len(poly)))) / 2


def _clip(subject, clip):
    """Sutherland-Hodgman: the part of convex polygon `subject` inside convex polygon `clip` (2D)."""
    def ccw(poly):
        a = sum(poly[i][0] * poly[i - 1][1] - poly[i - 1][0] * poly[i][1] for i in range(len(poly)))
        return poly if a < 0 else poly[::-1]   # the sum above is negative for counter-clockwise
    out = ccw(subject)
    clip = ccw(clip)
    for i in range(len(clip)):
        a, b = clip[i - 1], clip[i]
        def inside(p):
            return (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0]) >= -1e-9
        def cross(p, q):
            dx1, dy1, dx2, dy2 = b[0] - a[0], b[1] - a[1], q[0] - p[0], q[1] - p[1]
            den = dx1 * dy2 - dy1 * dx2
            t = ((p[0] - a[0]) * dy2 - (p[1] - a[1]) * dx2) / den
            return [a[0] + t * dx1, a[1] + t * dy1]
        inp, out = out, []
        for j in range(len(inp)):
            cur, prev = inp[j], inp[j - 1]
            if inside(cur):
                if not inside(prev):
                    out.append(cross(prev, cur))
                out.append(cur)
            elif inside(prev):
                out.append(cross(prev, cur))
        if not out:
            return []
    return out


def coplanar_faces(els, eps=COPLANAR_EPS, min_area=COPLANAR_AREA, opposite=False):
    """Every pair of drawn faces of different elements that lie in one plane, facing the same way,
    and overlap by more than `min_area`: they z-fight. Returns (name a, face a, name b, face b,
    area) sorted. With `opposite`, the pairs facing opposite ways instead (two elements pressed
    together), as (index a, face a, area a, index b, face b, area b, overlap)."""
    buckets = {}
    faces = []
    for ei, el in enumerate(els):
        for d, n, quad in drawn_faces(el):
            canon = next(x for x in n if abs(x) > 1e-6) > 0 if opposite else True
            m = n if canon else [-x for x in n]
            off = sum(m[i] * quad[0][i] for i in range(3))
            key = tuple(round(x, 3) + 0.0 for x in m)
            faces.append((ei, d, m, quad, off, canon))
            buckets.setdefault(key, []).append(len(faces) - 1)
    found = []
    for idxs in buckets.values():
        idxs.sort(key=lambda i: faces[i][4])
        for x, i in enumerate(idxs):
            ei, di, n, qa, oa, ca = faces[i]
            for j in idxs[x + 1:]:
                ej, dj, _, qb, ob, cb = faces[j]
                if ob - oa > eps:
                    break
                if ej == ei or (opposite and ca == cb):
                    continue
                # a 2D basis in the plane
                t = [1.0, 0.0, 0.0] if abs(n[0]) < 0.9 else [0.0, 1.0, 0.0]
                u = [n[1] * t[2] - n[2] * t[1], n[2] * t[0] - n[0] * t[2], n[0] * t[1] - n[1] * t[0]]
                ul = math.sqrt(sum(c * c for c in u))
                u = [c / ul for c in u]
                v = [n[1] * u[2] - n[2] * u[1], n[2] * u[0] - n[0] * u[2], n[0] * u[1] - n[1] * u[0]]
                pa = [[sum(p[k] * u[k] for k in range(3)), sum(p[k] * v[k] for k in range(3))] for p in qa]
                pb = [[sum(p[k] * u[k] for k in range(3)), sum(p[k] * v[k] for k in range(3))] for p in qb]
                inter = _clip(pa, pb)
                area = _poly_area(inter) if len(inter) >= 3 else 0.0
                if area <= min_area:
                    continue
                if opposite:
                    found.append((ei, di, _poly_area(pa), ej, dj, _poly_area(pb), area))
                else:
                    a, b = sorted([(els[ei].name, di), (els[ej].name, dj)])
                    found.append((a[0], a[1], b[0], b[1], round(area, 3)))
    return sorted(found)


def inset_face(el: El, d: str, delta: float):
    """Moves face `d` of `el` in by `delta` voxels, the opposite face staying where it is."""
    k, sgn = FACE_NORMAL[d]
    mag = abs(el.size[k])
    if mag <= delta * 2:
        raise ValueError(f"{el.name} is too thin to inset its {d} face")
    scale_uv(el, k, (mag - delta) / mag)
    el.size[k] = math.copysign(mag - delta, el.size[k])
    axis = [el.r[i][k] for i in range(3)]
    el.c = [el.c[i] - sgn * axis[i] * delta / 2 for i in range(3)]


def fix_coplanar(els, posed_fn, poses, inset=COPLANAR_INSET, rounds=12):
    """Ends the z-fighting: no two drawn faces may share a plane where they overlap. First every
    face pressed flat against an element of the same part (inside an opposite face of it, so the
    other element's body covers it) is removed: it can never be seen. Then of each remaining pair
    of faces in one plane, facing the same way, at any of `poses`, the smaller one (the inner part)
    is moved in by `inset`, until no pair is left; a stack of n such faces ends up stepped
    0, 1, ..., n-1 insets deep.

    `posed_fn(els, pose)` returns the elements posed (copies, in order); the first pose is the one
    at rest. Elements are changed in place. Returns ({pose: the pairs found before}, the number of
    faces removed)."""
    def posed_all(pose):
        return posed_fn(els, pose)
    before = {pose: coplanar_faces(posed_all(pose)) for pose in poses}
    rest = posed_all(poses[0])
    hidden = set()
    for ia, da, area_a, ib, db, area_b, overlap in coplanar_faces(rest, opposite=True):
        if els[ia].part != els[ib].part:
            continue
        if overlap >= area_a - 1e-3:
            hidden.add((ia, da))
        if overlap >= area_b - 1e-3:
            hidden.add((ib, db))
    for i, d in sorted(hidden):
        del els[i].faces[d]
    index = {el.name: i for i, el in enumerate(els)}
    for _ in range(rounds):
        moved = set()
        for pose in poses:
            for na, da, nb, db, _area in coplanar_faces(posed_all(pose)):
                if (na, da) in moved or (nb, db) in moved:
                    continue
                ea, eb = els[index[na]], els[index[nb]]
                def face_area(el, d):
                    k, _ = FACE_NORMAL[d]
                    return math.prod(abs(el.size[a]) for a in range(3) if a != k)
                # the smaller face is the inner part's; on a tie, the later name's
                inner = (nb, db) if (face_area(eb, db), nb) <= (face_area(ea, da), na) else (na, da)
                inset_face(els[index[inner[0]]], inner[1], inset)
                moved.add(inner)
        if not moved:
            break
    return before, len(hidden)


# ---------------------------------------------------------------- structure
def frame_floating(frame, ground=0.01):
    """Nothing floats: the frame elements joined to the ground (an element whose bottom is at or
    below `ground`) through elements that share a face with, or overlap, each other. Returns
    (the joined indices, the names of the rest, sorted)."""
    boxes = [el.aabb() for el in frame]
    aligned = [all(el.local_axis_for(k) is not None for k in range(3)) for el in frame]

    def joined(i, j):
        (alo, ahi), (blo, bhi) = boxes[i], boxes[j]
        ov = [min(ahi[k], bhi[k]) - max(alo[k], blo[k]) for k in range(3)]
        if min(ov) < -0.02:
            return False
        if aligned[i] and aligned[j]:
            return sum(o > 0.05 for o in ov) >= 2          # a shared face, not just an edge or a corner
        return obb_obb(frame[i], frame[j], eps=-0.03)
    seen = {i for i, (lo, _) in enumerate(boxes) if lo[1] <= ground}
    todo = list(seen)
    while todo:
        i = todo.pop()
        for j in range(len(frame)):
            if j not in seen and joined(i, j):
                seen.add(j)
                todo.append(j)
    return seen, sorted(frame[i].name for i in range(len(frame)) if i not in seen)


def bearing_margin(bearing: El, shaft_els, axis: int):
    """How far inside `bearing` the shaft elements (posed, as many poses as wanted) stay across the
    shaft, where they run through it along `axis`; 1e9 if none runs through it, negative if one
    sticks out."""
    b_lo, b_hi = bearing.aabb()
    worst = 1e9
    for el in shaft_els:
        lo, hi = el.aabb()
        if min(hi[axis], b_hi[axis]) - max(lo[axis], b_lo[axis]) <= 0.05:
            continue
        for k in range(3):
            if k != axis:
                worst = min(worst, lo[k] - b_lo[k], b_hi[k] - hi[k])
    return worst


def supports(frame, els_, axis, c, exclude=()):
    """The frame elements that carry a shaft: the shaft is the elements `els_` that its axis (along
    `axis`, through `c`, the two other coordinates in order) passes through; a support encloses
    the axis where it overlaps the shaft's length. Returns (their names, the shaft's span along
    the axis), or ([], None) if no element is on the axis."""
    u, v = [k for k in range(3) if k != axis]
    on = [e for e in els_ if (lambda lo, hi: lo[u] <= c[0] <= hi[u] and lo[v] <= c[1] <= hi[v])(*e.aabb())]
    if not on:
        return [], None
    amin, amax = min(e.aabb()[0][axis] for e in on), max(e.aabb()[1][axis] for e in on)
    found = []
    for f in frame:
        if f.name in exclude:
            continue
        lo, hi = f.aabb()
        if lo[u] + 0.05 < c[0] < hi[u] - 0.05 and lo[v] + 0.05 < c[1] < hi[v] - 0.05 and min(hi[axis], amax) - max(lo[axis], amin) > 0.05:
            found.append(f.name)
    return found, (amin, amax)


def euler_round_trip(els):
    """The worst matrix error of writing each element's rotation as Euler angles and back."""
    worst = 0.0
    for el in els:
        back = from_euler(*euler_xyz(el.r))
        worst = max(worst, max(abs(back[i][j] - el.r[i][j]) for i in range(3) for j in range(3)))
    return worst


def sight_glass(glass, oil: El):
    """What hides an oiler's level: a pane not in the Transparent pass (an opaque pass draws the glass
    texture solid), or a side of the oil (north, east, south, west) with no pane over it, its face seen
    through nothing. Empty when the level shows through glass from every side."""
    out = [f"{g.name} is not in the Transparent pass" for g in glass if g.render_pass != TRANSPARENT]
    lo, hi = oil.aabb()
    for side, axis, sign in (("north", 2, -1), ("east", 0, 1), ("south", 2, 1), ("west", 0, -1)):
        across = 2 - axis
        def covers(g):
            glo, ghi = g.aabb()
            beyond = glo[axis] >= hi[axis] if sign > 0 else ghi[axis] <= lo[axis]
            return beyond and glo[across] <= lo[across] and ghi[across] >= hi[across] and glo[1] <= lo[1] and ghi[1] >= hi[1]
        if not any(covers(g) for g in glass):
            out.append(f"no pane over the oil's {side} side")
    return out
