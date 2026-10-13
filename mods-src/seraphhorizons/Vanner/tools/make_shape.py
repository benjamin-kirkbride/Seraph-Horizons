"""Salvaged from origin/735-concentrator-model-v2 (Concentrator/tools/make_shape.py), not yet adapted:
the box helpers, the vanner's mechanism (build_vanner_moving, build_vanner), the vanilla chute profile
and its check against the game's file, and the soldered water line. The next commit makes it the
vanner's own generator (#924)."""

from __future__ import annotations

import argparse
import copy
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Machines" / "tools"))
sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Pipes" / "tools"))

from machinegen.checks import cell_boxes, cells_touched, with_lids  # noqa: E402
from machinegen.checks import fix_coplanar as fix_coplanar_posed  # noqa: E402
from machinegen.geometry import IDENT, El, flatten, rotate, translate  # noqa: E402
from machinegen.output import (reference_dumps, rig_dumps, round_matrix, shape_dumps, shift_cell,  # noqa: E402
                               shift_point, worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

import solderedpipe  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "Concentrator" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/Concentrator/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
DEG = math.pi / 180


VANILLA_CHUTE = {                            # game 1.22.7, shapes/block/metal/chute/straight.json (the "we" shape):
    "walls": ((4.0, 12.0, 4.0, 5.0),         # its walls across the run, (a0, a1, b0, b1) cell-local over the two
              (4.0, 12.0, 11.0, 12.0),       # axes across it: two full-width walls, two between them; 1 thick,
              (4.0, 5.0, 5.0, 11.0),         # an 8 x 8 tube centred in the block (its block's 4..12), a 6 x 6
              (11.0, 12.0, 5.0, 11.0)),      # bore. Its "ud" variant is the same turned upright (rotateZ 90)
    "texture": "game:block/metal/sheet/copper1",
    "shape": "shapes/block/metal/chute/straight.json",
}

# ---------------------------------------------------------------- level 4: the vanner
HEAD = (46.0, 9.1)                           # (x, y): the head roller's axis (the belt rises to the head)
FOOT = (22.0, 8.3)
ROLL_R = 2.5
BELT_T = 0.4
BELT_Z = (33.9, 41.1)
RAILS_Z = ((32.0, 33.5), (41.5, 43.0))
RAIL_Y = (7.0, 10.0)
RAIL_X = (18.0, 49.9)
VN_E = 0.5                                   # the shake: half a voxel either side
CS = (14.1, 43.9)                            # (y, z): the countershaft along x, by the south rail
CS_R = 0.7
CS_X = (17.2, 38.5)                          # the countershaft: the drive pulley, the two sheaves
CS_SHEAVES_X = (25.5, 34.6)
CS_PEDESTALS = ((19.4, 20.8), (28.6, 30.0), (36.9, 38.3), (40.0, 41.4), (50.6, 51.9))   # the last two the worm shaft's
WS_X = (38.7, 52.4)                          # the worm shaft, in line with the countershaft beyond its end
RATCHET_X = (38.8, 39.7)                     # the ratchet wheel on it: 12 teeth, module 0.5
RATCHET_N = 12
RATCHET_MOD = 0.5
WORM_N = 12                                  # the head roller's worm wheel: 12 teeth, module 0.6, a single-start worm
WHEEL_MOD = 0.6
WHEEL_R = WORM_N * WHEEL_MOD / 2             # its pitch radius, 3.6
WORM_R = CS[0] - HEAD[1] - WHEEL_R           # the worm's pitch radius: the countershaft over the wheel's top
WHEEL_Z = (43.2, 44.6)
RATCHET_R = 2.0                              # the ratchet's pitch radius (its teeth module RATCHET_MOD, 2 pi R / N apart)
PAWL_TRAVEL = 2 * VN_E                       # the pawl pushes the ratchet round by the shake's stroke each turn
RATCHET_RATIO = PAWL_TRAVEL / RATCHET_R / (2 * math.pi)   # radians of the ratchet per radian of the axle's travel
TANK_X = (42.0, 50.0)                        # the concentrate box under the head
TANK_Z = (34.3, 41.2)
TANK_Y = (4.8, 5.9)                          # its floor's top and its walls' top
TANK_SPOUT_X = (44.5, 47.5)
VFEED_X = (37.5, 42.5)                       # the vanner's feed box over the belt, under P3
VFEED_Y = (12.3, 13.5)
VSLIDES_X = (25.5, 33.0, 39.5)


# ---------------------------------------------------------------- box helpers
def skin(el, tex):
    """Every face takes `tex`, its UVs a region of the texture in proportion to the face's size."""
    axes = {"north": (0, 1), "south": (0, 1), "east": (2, 1), "west": (2, 1), "up": (0, 2), "down": (0, 2)}
    el.faces = {}
    for d, (u, v) in axes.items():
        w = min(abs(el.size[u]) * TEX / 16, TEX)
        h = min(abs(el.size[v]) * TEX / 16, TEX)
        el.faces[d] = {"texture": "#" + tex, "uv": [0.0, 0.0, w, h]}
    return el


def box(lo, hi, name, part, tex):
    c = [(lo[k] + hi[k]) / 2 for k in range(3)]
    return skin(El(name, [hi[k] - lo[k] for k in range(3)], c, [r[:] for r in IDENT], {}, part), tex)


def beam(lo, hi, name, part, tex, seg=16.0):
    """A long box split into pieces of at most `seg` along its longest axis (so no texture stretches)."""
    axis = max(range(3), key=lambda a: hi[a] - lo[a])
    n = max(1, math.ceil((hi[axis] - lo[axis]) / seg - 1e-9))
    out = []
    for i in range(n):
        l2, h2 = list(lo), list(hi)
        l2[axis] = lo[axis] + (hi[axis] - lo[axis]) * i / n
        h2[axis] = lo[axis] + (hi[axis] - lo[axis]) * (i + 1) / n
        out.append(box(l2, h2, f"{name}{i + 1}" if n > 1 else name, part, tex))
    return out


AX = {"x": 0, "y": 1, "z": 2}
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}   # a disc's angle 0 direction, and the in-plane direction it turns towards


def radial(axis, c, a0, a1, r0, r1, width, ang, name, part, tex):
    """A box from radius r0 to r1 about `axis` through c, a0..a1 along the axis, `width` across, at angle
    `ang` (radians, right-handed about +axis from the axis's reference direction)."""
    a = AX[axis]
    u, w = REF[axis]
    lo, hi = [0.0] * 3, [0.0] * 3
    lo[a], hi[a] = a0, a1
    lo[u], hi[u] = c[u] + r0, c[u] + r1
    lo[w], hi[w] = c[w] - width / 2, c[w] + width / 2
    el = box(lo, hi, name, part, tex)
    if abs(ang) > 1e-12:
        rotate([el], axis, math.degrees(ang), c)
    return el


def disc(axis, c, a0, a1, r, name, part, tex, k=4, phase=0.0):
    """A plain round part: k strips as long as the 2k-gon is across, 180/k degrees apart, each a hair
    shorter than the last so no two ends share a plane."""
    a = AX[axis]
    u, w = REF[axis]
    half = r * math.tan(math.pi / (2 * k))
    out = []
    st = min(0.012, (a1 - a0) / (4 * k))
    for i in range(k):
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[a], hi[a] = a0 + st * i, a1 - st * i
        lo[u], hi[u] = c[u] - r, c[u] + r
        lo[w], hi[w] = c[w] - half, c[w] + half
        el = box(lo, hi, f"{name}_{i + 1}" if k > 1 else name, part, tex)
        ang = phase + math.pi * i / k
        if abs(ang) > 1e-12:
            rotate([el], axis, math.degrees(ang), c)
        out.append(el)
    return out


def annulus(axis, c, a0, a1, r_in, r_out, n, name, part, tex, phase=0.0):
    """A ring of n boxes from r_in to r_out, each as wide as a side of the n-gon at r_out; every other
    one a hair shorter along the axis, so their ends never share a plane."""
    w = 2 * r_out * math.tan(math.pi / n)
    out = []
    for i in range(n):
        s = 0.02 if i % 2 else 0.0
        out.append(radial(axis, c, a0 + s, a1 - s, r_in, r_out, w, phase + TAU * i / n, f"{name}{i + 1}", part, tex))
    return out


def gear(axis, c, a0, a1, pitch_r, n, module, name, part, tex, phase=0.0, body_k=4):
    """A spur wheel: a polygon body to the root and n teeth of `module` (phase: angle of tooth 1)."""
    width = math.pi * module / 2
    root, tip = pitch_r - 1.25 * module, pitch_r + module
    body = disc(axis, c, a0, a1, root, f"{name}_body", part, tex, k=body_k, phase=phase)
    return body + [radial(axis, c, a0 + 0.03, a1 - 0.03, root - 0.25, tip, width, phase + TAU * i / n, f"{name}_tooth{i + 1}", part, tex)
                   for i in range(n)]


def pulley(axis, c, a0, a1, r, name, part, tex="steel", hub_r=1.3):
    """A plain pulley: a rim (a ring of 12) and a web to a hub. No teeth: it drives a belt."""
    out = annulus(axis, c, a0, a1, r - 0.7, r, 12, f"{name}_rim", part, tex)
    mid = (a0 + a1) / 2
    out += disc(axis, c, mid - 0.3, mid + 0.3, r - 0.65, f"{name}_web", part, tex, k=6)
    out += disc(axis, c, a0 + 0.1, a1 - 0.1, hub_r, f"{name}_hub", part, tex)
    return out


def strut(a, b, w, d, name, part, tex, axis="x"):
    """A bar from point a to point b in the plane normal to `axis`, `w` wide in that plane, `d` along it."""
    ia = AX[axis]
    u, v = [(1, 2), (2, 0), (0, 1)][ia]
    du, dv = b[u] - a[u], b[v] - a[v]
    length = math.hypot(du, dv)
    mid = [(a[i] + b[i]) / 2 for i in range(3)]
    lo, hi = list(mid), list(mid)
    lo[u], hi[u] = mid[u] - length / 2, mid[u] + length / 2
    lo[v], hi[v] = mid[v] - w / 2, mid[v] + w / 2
    lo[ia], hi[ia] = mid[ia] - d / 2, mid[ia] + d / 2
    el = box(lo, hi, name, part, tex)
    rotate([el], axis, math.degrees(math.atan2(dv, du)), mid)
    return el


def sloped(axis, a0, a1, y0, y1, pieces, part):
    """Boxes laid on a straight slope along `axis` ("x" or "z") from a0 (height y0) to a1 (height y1),
    either way along the axis. Each piece is (s0, s1, h0, h1, c0, c1, name, tex): s along the slope from
    the a0 end (0 .. its length), h above the slope's line, c across the axis (z for x, x for z). They are
    built level from a0 and turned about the a0 end's line, so a0's end stays where it is."""
    a = AX[axis]
    acr = 2 if axis == "x" else 0
    length = a1 - a0
    dirn = 1.0 if length >= 0 else -1.0
    out = []
    for s0, s1, h0, h1, c0, c1, name, tex in pieces:
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[a], hi[a] = sorted((a0 + dirn * s0, a0 + dirn * s1))
        lo[1], hi[1] = y0 + h0, y0 + h1
        lo[acr], hi[acr] = c0, c1
        out.append(box(lo, hi, name, part, tex))
    target = math.atan2(y1 - y0, abs(length))
    if abs(target) > 1e-12:
        if axis == "x":
            rotate(out, "z", math.degrees(dirn * target), (a0, y0, 0.0))
        else:
            rotate(out, "x", -math.degrees(dirn * target), (0.0, y0, a0))
    return out


def slope_len(a0, a1, y0, y1):
    return math.hypot(a1 - a0, y1 - y0)


def along(a0, a1, y0, y1, at):
    """The distance along a slope from its a0 end to the point over `at`."""
    return abs(at - a0) * slope_len(a0, a1, y0, y1) / abs(a1 - a0)


def floor_y(y0, y1, a0, a1, at):
    """A trough's floor top at `at` along its axis (its floor's top runs straight from y0 at a0 to y1 at a1)."""
    return y0 + (y1 - y0) * (at - a0) / (a1 - a0)


def under_slope(y0, y1, a0, a1, lo, hi, t):
    """The lowest point of a sloped board's underside over lo..hi along its axis (its top runs from y0 at
    a0 to y1 at a1, it is t thick), less a hundredth: where a leg or block under it stops."""
    ang = math.atan2(y1 - y0, abs(a1 - a0))
    return min(floor_y(y0, y1, a0, a1, lo), floor_y(y0, y1, a0, a1, hi)) - t / math.cos(ang) - 0.01


def trough(axis, a0, a1, y0, y1, c0, c1, side, name, part, tex="planks", t=LAUNDER_T, ends=(True, False), extra=()):
    """An open board trough along `axis` from a0 to a1 (either way), its floor's top falling or rising
    straight from y0 at a0 to y1 at a1, its outside c0..c1 across, sides `side` high over the floor,
    boards t thick; `ends` closes the a0 and a1 ends with an end board as high as the sides."""
    run = slope_len(a0, a1, y0, y1)
    pieces = [(0.0, run, -t, 0.0, c0 + t, c1 - t, f"{name}_floor", tex),
              (0.0, run, -t, side, c0, c0 + t, f"{name}_side1", tex),
              (0.0, run, -t, side, c1 - t, c1, f"{name}_side2", tex)]
    if ends[0]:
        pieces.append((0.0, t, 0.0, side, c0 + t, c1 - t, f"{name}_end1", tex))
    if ends[1]:
        pieces.append((run - t, run, 0.0, side, c0 + t, c1 - t, f"{name}_end2", tex))
    return sloped(axis, a0, a1, y0, y1, pieces + list(extra), part)


def belt(axis, c1, r1, c2, r2, a0, a1, name, part, tex="leather", t=0.3, n_wrap=8):
    """An open belt on two pulleys of radii r1, r2 about `axis` through c1, c2 (3-vectors; the axis
    coordinate is ignored), a0..a1 along the axis: two straight runs on the outer tangents and a wrap of
    short boxes round the far side of each pulley. Drawn still: a plain belt moving along its length looks
    the same."""
    ia = AX[axis]
    u, v = [(1, 2), (2, 0), (0, 1)][ia]
    p1, p2 = (c1[u], c1[v]), (c2[u], c2[v])
    d = math.hypot(p2[0] - p1[0], p2[1] - p1[1])
    base = math.atan2(p2[1] - p1[1], p2[0] - p1[0])
    beta = math.asin((r1 - r2) / d)
    out, normals = [], {}
    for side, sgn in (("a", 1), ("b", -1)):
        n = base + sgn * (math.pi / 2 - beta)
        normals[side] = n
        q1, q2 = [0.0] * 3, [0.0] * 3
        q1[u], q1[v] = p1[0] + (r1 + t / 2) * math.cos(n), p1[1] + (r1 + t / 2) * math.sin(n)
        q2[u], q2[v] = p2[0] + (r2 + t / 2) * math.cos(n), p2[1] + (r2 + t / 2) * math.sin(n)
        q1[ia] = q2[ia] = (a0 + a1) / 2
        out.append(strut(q1, q2, t, a1 - a0, f"{name}_run{side}", part, tex, axis=axis))
    for tag, (cu, cv), r, f, to in (("1", p1, r1, normals["a"], normals["b"] + TAU), ("2", p2, r2, normals["b"], normals["a"])):
        while to < f:
            to += TAU
        span = to - f
        n = max(3, int(round(n_wrap * span / math.pi)))
        for i in range(n):
            ang = f + span * (i + 0.5) / n
            seg = span / n * (r + t / 2) * 1.04
            q1, q2 = [0.0] * 3, [0.0] * 3
            for q, s in ((q1, -1), (q2, 1)):
                tang = ang + math.pi / 2
                q[u] = cu + (r + t / 2) * math.cos(ang) + s * seg / 2 * math.cos(tang)
                q[v] = cv + (r + t / 2) * math.sin(ang) + s * seg / 2 * math.sin(tang)
                q[ia] = (a0 + a1) / 2
            out.append(strut(q1, q2, t, a1 - a0 - (0.02 if i % 2 else 0.0), f"{name}_wrap{tag}_{i + 1}", part, tex, axis=axis))
    return out


def sheave(x, c, off, name, part):
    """An eccentric sheave keyed on a shaft along x through c = (y, z): a 12-sided disc SHEAVE_R across
    its flats, its centre `off` = (dy, dz) from the shaft's, SHEAVE_W thick about x."""
    sc = (0.0, c[0] + off[0], c[1] + off[1])
    return disc("x", sc, x - SHEAVE_W / 2, x + SHEAVE_W / 2, SHEAVE_R, name, part, "steel", k=6)


def yoke(x, c, e, kind, name, part):
    """The iron yoke a sheave of throw e runs in, about the shaft through c = (y, z), at rest: `kind` "y"
    (a horizontal slot: the yoke follows the sheave up and down, its top and bottom bars on the sheave) or
    "z" (an upright slot: it follows the sheave across, its north and south bars on it). Those bars clear
    the sheave's corners by a hundredth; the other pair clears its whole swing."""
    y, z = c
    t = YOKE_T
    x0, x1 = x - SHEAVE_W / 2 - 0.15, x + SHEAVE_W / 2 + 0.15
    rc = SHEAVE_RC + 0.01
    far = e + rc + YOKE_GAP
    if kind == "y":                              # top and bottom bars on the sheave; end bars beyond its swing in z
        return [box([x0, y + rc, z - far - t], [x1, y + rc + t, z + far + t], f"{name}_top", part, "iron"),
                box([x0, y - rc - t, z - far - t], [x1, y - rc, z + far + t], f"{name}_bottom", part, "iron"),
                box([x0, y - rc, z - far - t], [x1, y + rc, z - far], f"{name}_n", part, "iron"),
                box([x0, y - rc, z + far], [x1, y + rc, z + far + t], f"{name}_s", part, "iron")]
    return [box([x0, y - far - t, z - rc - t], [x1, y + far + t, z - rc], f"{name}_n", part, "iron"),
            box([x0, y - far - t, z + rc], [x1, y + far + t, z + rc + t], f"{name}_s", part, "iron"),
            box([x0, y + far, z - rc], [x1, y + far + t, z + rc], f"{name}_top", part, "iron"),
            box([x0, y - far - t, z - rc], [x1, y - far, z + rc], f"{name}_bottom", part, "iron")]


def yoke_bottom(c, e, kind):
    """The underside of a yoke's bottom bar at rest."""
    rc = SHEAVE_RC + 0.01
    return c[0] - rc - YOKE_T if kind == "y" else c[0] - (e + rc + YOKE_GAP) - YOKE_T


def build_infeed():
    """The infeed at the mill levels (`infeed`): vanilla's straight chute, upright, from the feed cell's top
    face down into the hopper: its cross-section, wall thickness, place in the block and texture exactly
    (VANILLA_CHUTE), so a chute placed in the cell above meets it with no gap or step. Its four walls, the
    two full-width ones and the two between them, as the game's shape has them."""
    p = "infeed"
    cx, cz = FEED_CELL[0] * B, FEED_CELL[2] * B
    top = (FEED_CELL[1] + 1) * B
    names = ("n", "s", "w", "e")
    out = []
    for name, (a0, a1, b0, b1) in zip(names, VANILLA_CHUTE["walls"]):
        out.append(box([cx + a0, CHUTE_Y0, cz + b0], [cx + a1, top, cz + b1], f"ch_wall_{name}", p, "chute"))
    return out


def chute_profile(els):
    """The infeed's walls where they meet the top face, cell-local (x0, x1, z0, z1), sorted; with the height
    of their tops."""
    cx, cz = FEED_CELL[0] * B, FEED_CELL[2] * B
    out, tops = [], set()
    for el in els:
        if el.part != "infeed":
            continue
        lo, hi = el.aabb()
        out.append(tuple(round(v, 4) for v in (lo[0] - cx, hi[0] - cx, lo[2] - cz, hi[2] - cz)))
        tops.add(round(hi[1], 4))
    return sorted(out), tops


def vanilla_profile():
    """VANILLA_CHUTE's walls as an upright chute has them over (x, z), sorted: the "we" shape's walls are over
    (y, z); turned upright (rotateZ 90 about the block's centre) its y becomes x, mirrored, which the square
    section does not mind."""
    return sorted((16.0 - a1, 16.0 - a0, b0, b1) for a0, a1, b0, b1 in VANILLA_CHUTE["walls"])


def read_vanilla_chute():
    """The game's own straight chute, if the game is here ($VINTAGE_STORY or ~/Games/vintagestory): its
    walls' sections over (y, z) and its texture, or None. The generator holds VANILLA_CHUTE to it."""
    import os
    for base in (os.environ.get("VINTAGE_STORY"), str(Path.home() / "Games" / "vintagestory")):
        if not base:
            continue
        f = Path(base) / "assets" / "survival" / VANILLA_CHUTE["shape"]
        if f.exists():
            shape = json.loads(f.read_text())
            walls = sorted((e["from"][1], e["to"][1], e["from"][2], e["to"][2]) for e in shape["elements"])
            runs = {(e["from"][0], e["to"][0]) for e in shape["elements"]}
            tex = "game:" + next(iter(shape["textures"].values()))
            return walls, runs, tex
    return None


# ---------------------------------------------------------------- builders: level 4, the vanner
CS_C = (0.0, CS[0], CS[1])


def run_y(x):
    """The belt's upper run's underside at x (from the foot roller's top to the head roller's top)."""
    return FOOT[1] + ROLL_R + (x - FOOT[0]) * (HEAD[1] - FOOT[1]) / (HEAD[0] - FOOT[0])


def belt_loop(name, part):
    """The endless belt round the foot and head rollers (both inside the loop): the upper and lower runs
    on their tangents and a wrap of short boxes round each roller's far side. Drawn still."""
    return belt("z", (FOOT[0], FOOT[1], 0.0), ROLL_R, (HEAD[0], HEAD[1], 0.0), ROLL_R, BELT_Z[0], BELT_Z[1], name, part,
                t=BELT_T, n_wrap=7)


def build_vanner_moving():
    """The vanner's drive pulley on the line shaft; the countershaft (its pulley, two sheaves); the worm
    shaft in line beyond it (the ratchet wheel, the worm); the shaking frame: two rails, the end ties, the
    rollers' bearings, the belt and its raised edges, the yokes the sheaves run in (upright slots: the frame
    follows them across), the pawl under the ratchet; the head roller with its worm wheel; the foot roller."""
    out = pulley("x", LS_C, *VPULLEY_X, VPULLEY_R, "vannerpulley", "vannerpulley")
    c = "vannershaft"
    hx = HEAD[0]
    out += disc("x", CS_C, CS_X[0], CS_X[1], CS_R, f"{c}_rod", c, "steel")
    out += pulley("x", CS_C, *VPULLEY_X, VPULLEY_R, f"{c}_pulley", c)
    for i, xs in enumerate(CS_SHEAVES_X, 1):
        out += sheave(xs, CS, (VN_E, 0.0), f"{c}_sheave{i}", c)
    for i, (px0, px1) in enumerate(CS_PEDESTALS, 1):
        pid = c if i <= 3 else "vannerworm"
        for j, xc in enumerate((px0 - 0.5, px1), 1):
            if (i, j) in ((3, 2), (4, 1)):
                continue                              # the countershaft's end and the worm shaft's start face the ratchet
            out += disc("x", CS_C, xc, xc + 0.5, 1.15, f"{pid}_collar{i}{j}", pid, "steel", k=4)
    # the worm shaft: its rod, the ratchet wheel at its west end, and the worm over the head roller's wheel,
    # a core and four thread rings pitched so a turn moves the wheel a tooth
    w = "vannerworm"
    out += disc("x", CS_C, WS_X[0], hx - 2.3, CS_R, f"{w}_rod", w, "steel")
    out += disc("x", CS_C, hx + 2.3, WS_X[1], CS_R, f"{w}_rod2", w, "steel")
    out += gear("x", CS_C, *RATCHET_X, RATCHET_R, RATCHET_N, RATCHET_MOD, f"{w}_ratchet", w, "steel", body_k=6)
    lead = math.pi * WHEEL_MOD
    out += disc("x", CS_C, hx - 2.3, hx + 2.3, WORM_R - 1.25 * WHEEL_MOD + 0.15, f"{w}_worm_core", w, "steel", k=4)
    for i in range(4):
        xt = hx - 1.5 * lead + i * lead
        ring = disc("x", CS_C, xt - 0.35, xt + 0.35, WORM_R + WHEEL_MOD * 0.8, f"{w}_worm_thread{i + 1}", w, "steel", k=4)
        rotate(ring, "z", -7.0, (xt, CS[0], CS[1]))
        out += ring
    f = "vannerframe"
    for j, (rz0, rz1) in enumerate(RAILS_Z, 1):
        out += beam([RAIL_X[0], RAIL_Y[0], rz0], [RAIL_X[1], RAIL_Y[1], rz1], f"{f}_rail{j}_", f, "oak")
    for i, (tx0, tx1) in enumerate(((RAIL_X[0], RAIL_X[0] + 0.6), (RAIL_X[1] - 0.8, RAIL_X[1])), 1):
        out.append(box([tx0, RAIL_Y[0] + 0.5, RAILS_Z[0][1]], [tx1, RAIL_Y[1], RAILS_Z[1][0]], f"{f}_tie{i}", f, "oak"))
    for name, (rx, ry) in (("head", HEAD), ("foot", FOOT)):
        for j, (rz0, rz1) in enumerate(RAILS_Z, 1):
            out.append(box([rx - 1.0, ry - 1.0, rz0 - 0.4 if j == 1 else rz0], [rx + 1.0, ry + 1.0, rz1 + (0.4 if j == 2 and name == "foot" else 0.0)],
                           f"{f}_{name}bearing{j}", f, "iron"))
    out += belt_loop(f"{f}_belt", f)
    for j, (ez0, ez1) in enumerate(((BELT_Z[0], BELT_Z[0] + 0.8), (BELT_Z[1] - 0.8, BELT_Z[1])), 1):
        a = [FOOT[0] + 0.4, run_y(FOOT[0] + 0.4) + BELT_T + 0.35, 0.0]
        b = [HEAD[0] - 0.4, run_y(HEAD[0] - 0.4) + BELT_T + 0.35, 0.0]
        el = strut(a, b, 0.7, ez1 - ez0 - 0.02, f"{f}_edge{j}", f, "leather", axis="z")
        el.c[2] = (ez0 + ez1) / 2
        out.append(el)
    for i, xs in enumerate(CS_SHEAVES_X, 1):
        ys = yoke(xs, CS, VN_E, "z", f"{f}_yoke{i}", f)
        out += ys
        north = next(e for e in ys if e.name.endswith("_n"))
        lo, hi = north.aabb()
        out.append(box([lo[0], RAIL_Y[1], lo[2]], [hi[0], lo[1], hi[2]], f"{f}_yoke{i}_hanger", f, "iron"))
    # the pawl: an iron bracket up from the south rail, its tip under the ratchet's lowest teeth; it pushes
    # them along with the shake, a tooth a stroke, and rides back over them
    rx0, rx1 = RATCHET_X
    tip = CS[0] - RATCHET_R - RATCHET_MOD - 0.05
    out.append(box([rx0, RAIL_Y[1], RAILS_Z[1][0] + 0.2], [rx1, tip - 0.6, RAILS_Z[1][1]], f"{f}_pawl_arm", f, "iron"))
    out.append(box([rx0, tip - 0.6, RAILS_Z[1][0] + 0.2], [rx1, tip, CS[1] + 0.9], f"{f}_pawl", f, "iron"))
    hc = (HEAD[0], HEAD[1], 0.0)
    out += disc("z", hc, BELT_Z[0] - 0.2, BELT_Z[1] + 0.2, ROLL_R, "vannerhead_drum", "vannerhead", "oak", k=6)
    out += disc("z", hc, RAILS_Z[0][0] - 0.3, WHEEL_Z[0] + 0.1, 0.5, "vannerhead_journal", "vannerhead", "steel")
    out += gear("z", hc, *WHEEL_Z, WHEEL_R, WORM_N, WHEEL_MOD, "vannerhead_wheel", "vannerhead", "steel", body_k=6)
    fc = (FOOT[0], FOOT[1], 0.0)
    out += disc("z", fc, BELT_Z[0] - 0.2, BELT_Z[1] + 0.2, ROLL_R, "vannerfoot_drum", "vannerfoot", "oak", k=6)
    out += disc("z", fc, RAILS_Z[0][0] - 0.3, RAILS_Z[1][1] + 0.3, 0.5, "vannerfoot_journal", "vannerfoot", "steel")
    return out


def build_vanner():
    """The fixed parts: slide posts with iron plates under the rails; the pedestals of the countershaft and
    the worm shaft; the concentrate box under the head roller on two bearers over launder S, its spout north
    into the concentrate launder; the feed box over the belt under the table's spout, hung from the water
    line's run beside it; the drive belt. Its wash water comes from the water line's nozzles."""
    p = "vanner"
    out = []
    for i, x in enumerate(VSLIDES_X, 1):
        for j, (rz0, rz1) in enumerate(RAILS_Z, 1):
            out.append(box([x - 1.2, RAIL_Y[0] - 0.6, rz0 - 0.6], [x + 1.2, RAIL_Y[0] - 0.02, rz1 + 0.6], f"vn_slide{i}{j}", p, "iron"))
            out.append(box([x - 0.8, 0.0, rz0 + 0.2], [x + 0.8, RAIL_Y[0] - 0.6, rz1 - 0.2], f"vn_slidepost{i}{j}", p, "oak"))
    y, z = CS
    for i, (x0, x1) in enumerate(CS_PEDESTALS, 1):
        out.append(box([x0, 0.0, 43.7], [x1, y - 1.3, 45.0], f"vn_pedestal{i}", p, "oak"))
        out.append(box([x0, y - 1.3, z - 1.3], [x1, y + 1.3, z + 1.3], f"vn_bearing{i}", p, "iron"))
    # the concentrate box under the head roller, on two bearers across launder S
    tx0, tx1 = TANK_X
    tz0, tz1 = TANK_Z
    fy, wy = TANK_Y
    t = 0.8
    out.append(box([tx0, fy - t, tz0], [tx1, fy, tz1], "vn_tank_floor", p, "planks"))
    out.append(box([tx0, fy, tz1 - t], [tx1, wy, tz1], "vn_tank_s", p, "planks"))
    out.append(box([tx0, fy, tz0 + t], [tx0 + t, wy, tz1 - t], "vn_tank_w", p, "planks"))
    out.append(box([tx1 - t, fy, tz0 + t], [tx1, wy, tz1 - t], "vn_tank_e", p, "planks"))
    sx0, sx1 = TANK_SPOUT_X
    out.append(box([tx0, fy, tz0], [sx0, wy, tz0 + t], "vn_tank_n1", p, "planks"))
    out.append(box([sx1, fy, tz0], [tx1, wy, tz0 + t], "vn_tank_n2", p, "planks"))
    out.append(box([sx0, fy, tz0], [sx1, fy + 0.6, tz0 + t], "vn_tank_lip", p, "planks"))
    sy = fy + 0.6
    out += [box([sx0, sy - 0.5, CL_Z[0] + 1.6], [sx1, sy, tz0], "vn_tank_spout", p, "planks"),
            box([sx0 - 0.5, sy - 0.5, CL_Z[0] + 1.6], [sx0, sy + 0.4, tz0], "vn_tank_spout_w", p, "planks"),
            box([sx1, sy - 0.5, CL_Z[0] + 1.6], [sx1 + 0.5, sy + 0.4, tz0], "vn_tank_spout_e", p, "planks")]
    for i, (bx0, bx1) in enumerate(((tx0 + 0.6, tx0 + 2.0), (tx1 - 2.0, tx1 - 0.6)), 1):
        out.append(box([bx0, fy - t - 0.9, 34.0], [bx1, fy - t, 43.4], f"vn_bearer{i}", p, "oak"))
        out.append(box([bx0, 0.0, 34.0], [bx1, fy - t - 0.9, 35.4], f"vn_bearer{i}_postn", p, "oak"))
        out.append(box([bx0, 0.0, 42.0], [bx1, fy - t - 0.9, 43.4], f"vn_bearer{i}_posts", p, "oak"))
    # the feed box over the belt, under the table's spout, on two posts by the south wall
    fx0, fx1 = VFEED_X
    fy0, fy1 = VFEED_Y
    bz0, bz1 = BELT_Z[0] + 1.4, BELT_Z[1] - 1.4           # inside the belt's raised edges, through the shake
    out.append(box([fx0, fy0, bz0], [fx1, fy0 + 0.4, bz1], "vn_fb_floor", p, "planks"))
    out.append(box([fx1 - 0.6, fy0 + 0.4, bz0], [fx1, fy1, bz1], "vn_fb_e", p, "planks"))
    out.append(box([fx0, fy0 + 0.4, bz0], [fx1 - 0.6, fy1, bz0 + 0.6], "vn_fb_n", p, "planks"))
    out.append(box([fx0, fy0 + 0.4, bz1 - 0.6], [fx1 - 0.6, fy1, bz1], "vn_fb_s", p, "planks"))
    vx0 = VAN_X - PIPE_HALF
    out.append(box([fx1 - 0.6, fy1, 36.0], [vx0, VAN_Y - PIPE_HALF + 1.6, 39.0], "vn_fb_hanger", p, "iron"))
    out += belt("x", LS_C, VPULLEY_R, CS_C, VPULLEY_R, VPULLEY_X[0] + 0.15, VPULLEY_X[1] - 0.15, "vn_belt", p)
    return out


# ---------------------------------------------------------------- builders: the water line (soldered pipe)
def pipe_boxes():
    """The water line as UnifiedPipes' soldered pipe (solderedpipe's boxes, build-frame voxels): the inlet
    from the water face, on the axis of the ppex pipe beyond it, to a tee; west from the tee to a closed hub
    over the head of the line; east from it to an elbow, down to a second elbow under the line shaft, and
    south in two lengths, a joint at their seam, to its end over the belt, plugged with solder. A nozzle
    under the head of the line's hub, one under the tee (the table's), three along the vanner's run. As in
    the rosser's drip, the inlet has no joint at the face: the pipe beyond brings its own."""
    h = PIPE_HALF
    x, y = WATER_IN
    z = TEE_Z
    out = solderedpipe.tube("inlet", 2, 0.0, z - h, (x, y))
    out += solderedpipe.hub("tee", (x, y, z), ("north", "west", "east"))
    out += solderedpipe.tube("head", 0, HEAD_OUT_X + h, x - h, (z, y))
    out += solderedpipe.hub("headend", (HEAD_OUT_X, y, z), ("east",))
    out += solderedpipe.tube("cross", 0, x + h, VAN_X - h, (z, y))
    out += solderedpipe.hub("elbow1", (VAN_X, y, z), ("west", "down"))
    out += solderedpipe.tube("drop", 1, VAN_Y + h, y - h, (VAN_X, z))
    out += solderedpipe.hub("elbow2", (VAN_X, VAN_Y, z), ("up", "south"))
    out += solderedpipe.tube("van1", 2, z + h, VAN_SEAM, (VAN_X, VAN_Y))
    out += solderedpipe.tube("van2", 2, VAN_SEAM, VAN_Z1, (VAN_X, VAN_Y))
    out += solderedpipe.joint("joint_van", 2, VAN_SEAM, (VAN_X, VAN_Y))
    i_ = h - solderedpipe.WALL
    out.append(solderedpipe.Box("cap", [VAN_X - i_, VAN_Y - i_, VAN_Z1 - solderedpipe.WALL], [VAN_X + i_, VAN_Y + i_, VAN_Z1],
                                solderedpipe.SOLDER))
    noz = [("nozzle_head", HEAD_NOZZLE_X, y, z), ("nozzle_table", x, y, TABLE_OUT_Z)]
    noz += [(f"nozzle_van{i}", VAN_X, VAN_Y, zn) for i, zn in enumerate(VAN_NOZZLES_Z, 1)]
    for name, nx, ny, nz in noz:
        out.append(solderedpipe.Box(name, [nx - 0.6, ny - h - NOZZLE_Y, nz - 0.6], [nx + 0.6, ny - h, nz + 0.6], solderedpipe.BODY))
    return solderedpipe.cull_pressed(out)


def build_pipes(metal_=PIPE_METALS[0]):
    """The water line as elements of the part `pipe<metal_>` (`pipe_boxes`): the body in that metal's pipe
    texture, the joints in solder, each face's UVs solderedpipe's on this model's texture scale. Built in
    copper; `pipe_copies` makes lead."""
    p = f"pipe{metal_}"
    codes = {solderedpipe.BODY: f"#{p}", solderedpipe.SOLDER: "#solder"}
    out = []
    for b in pipe_boxes():
        size = b.size()
        faces = {d: {"texture": codes[b.role], "uv": solderedpipe.uv(d, size, TEX / 16)} for d in b.faces}
        out.append(El(f"{p}_{b.name}", size, [(b.lo[k] + b.hi[k]) / 2 for k in range(3)], [row[:] for row in IDENT], faces, p))
    return out


def pipe_copies(els):
    """The water line in the other metals: the first metal's elements as they are (after the z-fighting
    fix, so every copy has the same insets), renamed and in their own metal's texture; the joints stay
    solder. Only one metal's are ever drawn, so the copies are not checked against each other."""
    first = PIPES[0]
    out = []
    for m in PIPE_METALS[1:]:
        for el in els:
            if el.part == first:
                c = el.clone(f"pipe{m}" + el.name[len(first):], f"pipe{m}")
                for face in c.faces.values():
                    if face["texture"] == f"#{first}":
                        face["texture"] = f"#pipe{m}"
                out.append(c)
    return out

# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


WORM_RATIO = RATCHET_RATIO                   # the worm shaft turns with the ratchet
HEAD_RATIO = -WORM_RATIO / WORM_N            # the head roller: its top runs east (up the belt, to the head), always
FOOT_RATIO = HEAD_RATIO                      # the same radius, the same belt


def rot(axis, c, ratio, travel=False):
    """A rotate about `axis` through c (x, y, z voxels) by ratio x theta (or x psi, the travel)."""
    d = {"type": "rotate", "axis": axis, "pivot": pt(*c), "ratio": r6(ratio)}
    if travel:
        d["input"] = "travel"
    return d


def slide(axis, amplitude):
    return {"type": "slide", "axis": axis, "amplitude": r6(amplitude / B), "ratio": 1.0}


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


