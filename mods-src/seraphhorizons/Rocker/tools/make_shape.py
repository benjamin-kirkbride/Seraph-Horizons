#!/usr/bin/env python3
"""Generate the rocker's shapes, rig and reference poses.

The rocker (a cradle) is the hand concentrator of the 1850s gold rush: an open-topped oak box on two
curved rockers, its floor falling gently to its open foot, with low riffles across it. A hopper (the
riddle box) sits on the head of the box, its bottom an iron riddle plate; under it an inclined canvas
apron on a frame catches what falls through the riddle and carries it back to the head of the box, and
the water washes it down over the riffles and out at the foot. The operator rocks it from the side by an
upright handle. Here the box rolls on its rockers on two oak sills, and water comes either by bucket,
poured into the hopper, or by a Pipes and Power Expanded pipe on the south face, through a lead
gooseneck on a post whose spout stands over the hopper. Everything is built here from plain boxes; no
other mod's model is used.

It writes, deterministically,

    rocker.json         the whole rocker, every moving part        (assets/.../shapes/block/)
    rocker_frame.json   the static frame only: the sills (block)    (assets/.../shapes/block/)
    rocker-rig.json     cell, anchors and the part rig              (assets/.../config/)
    rig-reference.json  every part's matrix at a grid of poses      (tests/Rocker/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_rocker.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the
cell's north-west-bottom corner (the "build frame"); the rocker is one cell, the controller [0,0,0], so
the shipped files are the build frame divided by 16. The box runs along x: its head (the hopper, under
the spout) at the east end, its open foot at the west end, where the tailings leave; the operator stands
on the north side, at the handle, and the pipe comes in on the south face.

The rig's one input, as this machine uses it (README "Rig schema"):

    theta  the rocking, the hold-to-work clock: one turn is one rock, over to the south and back over to
           the north. The cradle swings about its rocking axis by ROCK_DEG sin(theta) and slides south by
           ROCKER_R times that angle, so its rockers roll on the sills without slipping.
"""

from __future__ import annotations

import argparse
import copy
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Machines" / "tools"))
sys.path.insert(0, str(Path(__file__).resolve().parent))

from machinegen.checks import cell_boxes, cells_touched, with_lids  # noqa: E402
from machinegen.checks import fix_coplanar as fix_coplanar_posed  # noqa: E402
from machinegen.geometry import IDENT, TRANSPARENT, El, flatten, mvec, rot, rotate, translate  # noqa: E402
from machinegen.output import (reference_dumps, rig_dumps, round_matrix, shape_dumps, shift_cell,  # noqa: E402
                               shift_point, worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "Rocker" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/Rocker/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
DEG = math.pi / 180

# ---------------------------------------------------------------- the box, the cell
CELLS_X, CELLS_Y, CELLS_Z = 1, 1, 1          # one cell: the period cradle is a metre long and half a metre wide
ORIGIN_CELL = (0, 0, 0)                      # the controller, the only cell

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "iron": "game:block/metal/plate/iron",
    "riddle": "game:block/metal/mesh4",          # the riddle plate: the game's rusty iron mesh, cut out (it shows the apron through it)
    "canvas": "game:block/linen",                # the apron
    "pipe": "game:block/metal/sheet-plain/lead4",   # the gooseneck: lead pipe, in the pipes' own lead texture
    "water": "game:block/liquid/water",
    "charge": "game:block/stone/gravel/granite",       # a charge in the hopper: the renderer sets it to the material's texture
    "concentrate": "game:block/stone/sand/basalt",     # the heavy sand behind the riffles: the renderer sets it to the ore's
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the sills and the rockers
SILL_TOP = 1.25                              # two oak sills on the ground, across the box under its rockers
SILL_Z = (2.5, 13.5)
SILL_HALF_X = 1.0
ROCKER_X = (2.6, 12.2)                       # the foot and head rockers' middles (x)
ROCKER_T = 1.2                               # their thickness (x): the top bar's; the running facets alternate a hair thinner
ROCKER_R = 10.0                              # the running faces' radius
ROCKER_HALF = 5.25                           # half the rockers' length (z) about the rocking axis
ROCKER_FACETS = 6                            # chords of the running face: an even number, so one corner is at the bottom
ROCKER_BAR = 1.0                             # the top bar's depth, which the floor is nailed to
ROCKER_EMBED = 0.1                           # the bars' tops sit this far up into the floor's underside
AXIS_Z = 8.0                                 # the rocking axis (z): the cell's middle
AXIS_Y = SILL_TOP + ROCKER_R                 # the running faces' centre: it moves only along z as the rockers roll
ROCK_DEG = 10.0                              # the rock, each way

# ---------------------------------------------------------------- the cradle: the box, built level, then tilted
BOX_X = (0.6, 15.4)                          # from the open foot (west) to the head board's outer face (east)
BOX_Z = (4.0, 12.0)                          # outside the sides: 8 wide
BOARD = 0.6                                  # the sides' thickness
IN_Z = (BOX_Z[0] + BOARD, BOX_Z[1] - BOARD)  # inside the sides
FLOOR_Y0 = 3.75                              # the floor's underside at the foot
FLOOR_T = 0.75
FLOOR_TOP = FLOOR_Y0 + FLOOR_T
SIDE_TOP = FLOOR_TOP + 5.0                   # sides 5 voxels (0.3 m) over the floor
HEAD_T = 0.6                                 # the head board, on the floor between the sides
SLOPE = 3.5                                  # degrees: the whole box is tilted so the floor falls to the foot
TILT_PIVOT = (BOX_X[0], FLOOR_Y0, AXIS_Z)    # the foot's bottom edge stays put

# ---------------------------------------------------------------- the riffles, and what lies at them
RIFFLE_X = (3.0, 6.2, 9.6)                   # the riffles' foot-side faces (x, level frame): two in the open, one under the apron
RIFFLE_W, RIFFLE_H = 0.6, 0.6
POOL_L = 1.2                                 # the water standing behind (upstream of) a riffle
FILM = 0.15                                  # the sheet of water running over the floor
CONC_L, CONC_H = 1.0, 0.35                   # the heavy sand caught behind a riffle

# ---------------------------------------------------------------- the apron (level frame)
APRON_HI = (7.5, 9.2)                        # (x, y): its canvas top at the high end, under the hopper's foot wall
APRON_LO = (14.2, 6.0)                       # at the low end, short of the head board: it drops onto the floor's head
APRON_RAIL = 0.6                             # the rails' depth (in their plane) and width (z), against the sides
CANVAS_T = 0.12
APRON_WATER = 0.1

# ---------------------------------------------------------------- the hopper (the riddle box; level frame)
HOP_X = (7.4, 15.4)                          # on the head half, sitting on the sides and over the head board
HOP_WALL = 0.5
HOP_H = 2.5                                  # walls 2.5 high (the period hopper was 4 to 6 inches deep)
RIDDLE_T = 0.3                               # the iron plate, between the walls at their foot
HOP_WATER = 0.5                              # water standing on the plate while it is washed
CLEAT_X = ((7.9, 8.7), (14.0, 14.8))         # cleats outside the hopper's long walls, over the box's sides: they locate it
CLEAT_T, CLEAT_DOWN, CLEAT_UP = 0.4, 1.0, 0.6     # their thickness, and how far they reach down the side and up the wall
CHARGE_X = 11.4                              # a charge heaped on the plate: its middle (x)
CHARGE = ((6.2, 0.9), (4.4, 0.8), (2.4, 0.6))   # (square, height) of each layer, bottom up: heaped a little over the walls

# ---------------------------------------------------------------- the handle (built upright, not tilted)
HANDLE_X = (10.4, 11.4)
HANDLE_Z = (BOX_Z[0] - 1.0, BOX_Z[0])        # against the north side, where the operator stands
HANDLE_TOP = 14.5                            # a hand's height for a standing player
BANDS = (1.2, 3.8)                           # the iron bands, this far over the handle's foot
BAND_H = 0.45

# ---------------------------------------------------------------- the water inlet and spout (static)
FACE = 16.0                                  # the water face: the cell's south face
PPEX_PIPE = (5.0, 11.0)                      # Pipes and Power Expanded's pipe: a square tube 6 across, its block's 5..11
PIPE_AXIS = (8.0, 8.0)                       # (x, y): its axis, the water face's middle, where the inlet ends
UNION = 1.0                                  # half the small square union on the inlet's end, at the face
UNION_T = 0.3
PIPE_H = 0.5                                 # half the gooseneck's section: a 1-voxel lead pipe
PIPE_Y = PIPE_AXIS[1]                        # the run from the inlet: the face's middle
PIPE_Z = FACE - UNION_T - 0.075 - PIPE_H     # the run, the riser and the post (z): just inside the face, past the union
SPOUT_X = 13.3                               # the riser and the arm (x): over the hopper's head half
ARM_Y = 14.5                                 # the arm over the hopper (y)
NOZZLE = 0.4                                 # the spout's half section, over the rocking axis
NOZZLE_L = 0.5
POST_X = (SPOUT_X + PIPE_H, SPOUT_X + PIPE_H + 1.4)
POST_TOP = ARM_Y - 0.2                       # its top beside the arm
CLIP_Y = (10.0, 12.9)                        # iron clips round the riser and the post
CLIP_H = 0.4
STREAM_HALF = 0.175                          # the stream from the spout while piped water runs
STREAM_FOOT = 11.04                          # its foot, in the water on the riddle plate at every point of the rock

SLOPE_TAN = math.tan(SLOPE * DEG)


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


def strut_xy(a, b, w, z0, z1, name, part, tex):
    """A bar from point a to point b in the x-y plane (a, b as (x, y)), `w` deep in that plane, from z0 to z1."""
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy)
    mid = [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (z0 + z1) / 2]
    el = box([mid[0] - length / 2, mid[1] - w / 2, z0], [mid[0] + length / 2, mid[1] + w / 2, z1], name, part, tex)
    rotate([el], "z", math.degrees(math.atan2(dy, dx)), mid)
    return el


def tilt(els):
    """The cradle's elements, built level, tilted about z so the floor falls to the foot."""
    return rotate(els, "z", SLOPE, TILT_PIVOT)


def tilt_point(p):
    m = rot("z", SLOPE)
    rel = [p[k] - TILT_PIVOT[k] for k in range(3)]
    return [TILT_PIVOT[k] + mvec(m, rel)[k] for k in range(3)]


def untilt_point(p):
    m = rot("z", -SLOPE)
    rel = [p[k] - TILT_PIVOT[k] for k in range(3)]
    return [TILT_PIVOT[k] + mvec(m, rel)[k] for k in range(3)]


def underside(x):
    """The tilted floor's underside (y) over world x."""
    return FLOOR_Y0 + (x - BOX_X[0]) * SLOPE_TAN


def apron_normal():
    """The apron's upward normal in the x-y plane (level frame)."""
    dx, dy = APRON_LO[0] - APRON_HI[0], APRON_LO[1] - APRON_HI[1]
    n = math.hypot(dx, dy)
    return (-dy / n, dx / n)


def apron_line(off):
    """The apron's top line moved `off` along its normal: its (high, low) ends as (x, y)."""
    nx, ny = apron_normal()
    return ((APRON_HI[0] + off * nx, APRON_HI[1] + off * ny), (APRON_LO[0] + off * nx, APRON_LO[1] + off * ny))


def facet_angles():
    """The running faces' corners, as angles (radians) from the bottom, positive towards the south."""
    end = math.asin(ROCKER_HALF / ROCKER_R)
    return [-end + 2 * end * i / ROCKER_FACETS for i in range(ROCKER_FACETS + 1)]


def facet_gap():
    """The most a chord of the running face lies inside its circle: how far a rocker can stand off its sill."""
    half = (facet_angles()[1] - facet_angles()[0]) / 2
    return ROCKER_R * (1 - math.cos(half))


# ---------------------------------------------------------------- builders
def build_sills():
    """The frame: two oak sills on the ground, across the box, under the rockers."""
    out = []
    for name, xr in (("foot", ROCKER_X[0]), ("head", ROCKER_X[1])):
        out.append(box([xr - SILL_HALF_X, 0.0, SILL_Z[0]], [xr + SILL_HALF_X, SILL_TOP, SILL_Z[1]], f"fr_sill_{name}", "frame", "oak"))
    return out


def build_rockers():
    """Two oak rockers across the box: a top bar nailed under the floor and a curved running face of
    ROCKER_FACETS chords inscribed in the circle of ROCKER_R about the rocking axis, one corner at the bottom
    resting on the sill. Built upright (the box above them is tilted, so the head rocker is the taller)."""
    out = []
    angles = facet_angles()
    half = (angles[1] - angles[0]) / 2
    chord = 2 * ROCKER_R * math.sin(half)
    for name, xr in (("foot", ROCKER_X[0]), ("head", ROCKER_X[1])):
        top = underside(xr) + ROCKER_EMBED
        mid_bar = top - ROCKER_BAR / 2
        out.append(box([xr - ROCKER_T / 2, top - ROCKER_BAR, AXIS_Z - ROCKER_HALF],
                       [xr + ROCKER_T / 2, top, AXIS_Z + ROCKER_HALF], f"cradle_rocker_{name}_bar", "cradle", "oak"))
        for i in range(ROCKER_FACETS):
            m = (angles[i] + angles[i + 1]) / 2
            my = AXIS_Y - ROCKER_R * math.cos(half) * math.cos(m)
            mz = AXIS_Z + ROCKER_R * math.cos(half) * math.sin(m)
            h = (mid_bar - my) / math.cos(m)                 # up the inward normal, to the bar's middle
            t = ROCKER_T - (0.1 if i % 2 == 0 else 0.14)     # alternate a hair, so neighbouring facets share no plane
            c = [xr, my + h / 2 * math.cos(m), mz - h / 2 * math.sin(m)]
            el = box([c[0] - t / 2, c[1] - h / 2, c[2] - chord / 2], [c[0] + t / 2, c[1] + h / 2, c[2] + chord / 2],
                     f"cradle_rocker_{name}_facet{i + 1}", "cradle", "oak")
            rotate([el], "x", -math.degrees(m), c)
            out.append(el)
    return out


def build_box():
    """The cradle proper, level: the floor, two sides and the head board; the foot is open."""
    p = "cradle"
    return [
        box([BOX_X[0], FLOOR_Y0, IN_Z[0]], [BOX_X[1], FLOOR_TOP, IN_Z[1]], "cradle_floor", p, "oak"),
        box([BOX_X[0], FLOOR_Y0, BOX_Z[0]], [BOX_X[1], SIDE_TOP, IN_Z[0]], "cradle_side_n", p, "oak"),
        box([BOX_X[0], FLOOR_Y0, IN_Z[1]], [BOX_X[1], SIDE_TOP, BOX_Z[1]], "cradle_side_s", p, "oak"),
        box([BOX_X[1] - HEAD_T, FLOOR_TOP, IN_Z[0]], [BOX_X[1], SIDE_TOP, IN_Z[1]], "cradle_headboard", p, "oak"),
    ]


def build_riffles():
    """Low oak bars across the floor (level frame)."""
    return [box([x, FLOOR_TOP, IN_Z[0]], [x + RIFFLE_W, FLOOR_TOP + RIFFLE_H, IN_Z[1]], f"riffle_{i + 1}", "riffles", "oak")
            for i, x in enumerate(RIFFLE_X)]


def build_apron():
    """The apron (level frame): two oak rails against the sides, falling from under the hopper's foot wall
    towards the head, a canvas stretched between them, and an oak bar under each end of it."""
    out = []
    (hx, hy), (lx, ly) = apron_line(-APRON_RAIL / 2)
    for s, (z0, z1) in (("n", (IN_Z[0], IN_Z[0] + APRON_RAIL)), ("s", (IN_Z[1] - APRON_RAIL, IN_Z[1]))):
        out.append(strut_xy((hx, hy), (lx, ly), APRON_RAIL, z0, z1, f"apron_rail_{s}", "apron", "oak"))
    (hx, hy), (lx, ly) = apron_line(-CANVAS_T / 2)
    out.append(strut_xy((hx, hy), (lx, ly), CANVAS_T, IN_Z[0] + APRON_RAIL, IN_Z[1] - APRON_RAIL, "apron_canvas", "apron", "canvas"))
    nx, ny = apron_normal()
    tx, ty = APRON_LO[0] - APRON_HI[0], APRON_LO[1] - APRON_HI[1]
    tl = math.hypot(tx, ty)
    for name, (x, y), inward in (("hi", APRON_HI, 0.5), ("lo", APRON_LO, -0.5)):
        # the end bars: square, under the canvas and set in from its ends, between the rails, square to the apron
        d = 0.55
        c = [x - (CANVAS_T + d / 2) * nx + inward * tx / tl, y - (CANVAS_T + d / 2) * ny + inward * ty / tl, AXIS_Z]
        el = box([c[0] - d / 2, c[1] - d / 2, IN_Z[0] + APRON_RAIL], [c[0] + d / 2, c[1] + d / 2, IN_Z[1] - APRON_RAIL],
                 f"apron_bar_{name}", "apron", "oak")
        rotate([el], "z", math.degrees(math.atan2(ty, tx)), c)
        out.append(el)
    return out


def build_hopper():
    """The riddle box (level frame): four oak walls standing on the sides over the head half, cleats
    outside its long walls over the box's sides; and its iron riddle plate between the walls at their foot."""
    y0, y1 = SIDE_TOP, SIDE_TOP + HOP_H
    (x0, x1), (z0, z1), w = HOP_X, BOX_Z, HOP_WALL
    out = [
        box([x0, y0, z0], [x1, y1, z0 + w], "hopper_wall_n", "hopper", "oak"),
        box([x0, y0, z1 - w], [x1, y1, z1], "hopper_wall_s", "hopper", "oak"),
        box([x0, y0, z0 + w], [x0 + w, y1, z1 - w], "hopper_wall_foot", "hopper", "oak"),
        box([x1 - w, y0, z0 + w], [x1, y1, z1 - w], "hopper_wall_head", "hopper", "oak"),
    ]
    for i, (cx0, cx1) in enumerate(CLEAT_X):
        out.append(box([cx0, y0 - CLEAT_DOWN, z0 - CLEAT_T], [cx1, y0 + CLEAT_UP, z0], f"hopper_cleat_n{i + 1}", "hopper", "oak"))
        out.append(box([cx0, y0 - CLEAT_DOWN, z1], [cx1, y0 + CLEAT_UP, z1 + CLEAT_T], f"hopper_cleat_s{i + 1}", "hopper", "oak"))
    out.append(box([x0 + w, y0, z0 + w], [x1 - w, y0 + RIDDLE_T, z1 - w], "riddle_plate", "riddle", "riddle"))
    return out


def riddle_box():
    """The riddle plate's top, inside the hopper's walls (level frame): (x0, x1), (z0, z1), y."""
    return (HOP_X[0] + HOP_WALL, HOP_X[1] - HOP_WALL), (BOX_Z[0] + HOP_WALL, BOX_Z[1] - HOP_WALL), SIDE_TOP + RIDDLE_T


def build_handle():
    """The upright oak handle the operator rocks it by, against the north side and the hopper's north wall,
    held by two iron bands. Built upright: it rides the cradle, so it leans with the rock."""
    x0, x1 = HANDLE_X
    foot = underside((x0 + x1) / 2) + 0.6
    out = [box([x0, foot, HANDLE_Z[0]], [x1, HANDLE_TOP, HANDLE_Z[1]], "handle_stick", "handle", "oak")]
    for i, up in enumerate(BANDS):
        out.append(box([x0 - 0.1, foot + up, HANDLE_Z[0] - 0.1], [x1 + 0.1, foot + up + BAND_H, HANDLE_Z[1]],
                       f"handle_band{i + 1}", "handle", "iron"))
    return out


def build_water():
    """Water while it is washed (level frame, but the curtain off the foot, built upright): standing on the
    riddle plate, running down the apron and off its low end, a sheet down the floor with pools behind the
    riffles, and a curtain off the open foot."""
    out = []
    (rx0, rx1), (rz0, rz1), ry = riddle_box()
    out.append(box([rx0, ry, rz0], [rx1, ry + HOP_WATER, rz1], "water_hopper", "water", "water"))
    (hx, hy), (lx, ly) = apron_line(APRON_WATER / 2)
    out.append(strut_xy((hx, hy), (lx, ly), APRON_WATER, IN_Z[0] + APRON_RAIL + 0.1, IN_Z[1] - APRON_RAIL - 0.1,
                        "water_apron", "water", "water"))
    lx_end = APRON_LO[0] + 0.05
    out.append(box([lx_end, FLOOR_TOP + FILM, IN_Z[0] + APRON_RAIL + 0.1], [lx_end + 0.3, APRON_LO[1] - 0.05, IN_Z[1] - APRON_RAIL - 0.1],
                   "water_apronfall", "water", "water"))
    # the floor: a sheet from the head board to the foot, broken by the riffles, a pool behind each
    head_in = BOX_X[1] - HEAD_T
    edges = [BOX_X[0]]
    for x in RIFFLE_X:
        edges += [x, x + RIFFLE_W, x + RIFFLE_W + POOL_L]
    edges.append(head_in)
    out.append(box([edges[0], FLOOR_TOP, IN_Z[0]], [edges[1], FLOOR_TOP + FILM, IN_Z[1]], "water_film1", "water", "water"))
    for i in range(len(RIFFLE_X)):
        a, b, c = edges[3 * i + 2], edges[3 * i + 3], edges[3 * i + 4]
        out.append(box([a, FLOOR_TOP, IN_Z[0]], [b, FLOOR_TOP + RIFFLE_H - 0.08, IN_Z[1]], f"water_pool{i + 1}", "water", "water"))
        out.append(box([b, FLOOR_TOP, IN_Z[0]], [c, FLOOR_TOP + FILM, IN_Z[1]], f"water_film{i + 2}", "water", "water"))
    tilt(out)
    # the curtain off the foot, falling plumb from the lip towards the ground
    lip = tilt_point([BOX_X[0], FLOOR_TOP, AXIS_Z])
    out.append(box([0.2, 0.8, IN_Z[0] + 0.2], [lip[0] - 0.01, lip[1] + FILM - 0.05, IN_Z[1] - 0.2], "water_footfall", "water", "water"))
    for el in out:
        el.render_pass = TRANSPARENT
    return out


def build_charge():
    """A charge heaped on the riddle plate (level frame): three layers, each narrower."""
    (_, _), (rz0, rz1), y = riddle_box()
    zc = (rz0 + rz1) / 2
    out = []
    for i, (s, h) in enumerate(CHARGE):
        out.append(box([CHARGE_X - s / 2, y, zc - s / 2], [CHARGE_X + s / 2, y + h, zc + s / 2], f"charge_{i + 1}", "charge", "charge"))
        y += h
    return out


def build_concentrate():
    """The heavy sand caught behind each riffle (level frame), under the pool."""
    return [box([x + RIFFLE_W, FLOOR_TOP, IN_Z[0]], [x + RIFFLE_W + CONC_L, FLOOR_TOP + CONC_H, IN_Z[1]], f"conc_{i + 1}", "concentrate", "concentrate")
            for i, x in enumerate(RIFFLE_X)]


def nozzle_mouth():
    return (SPOUT_X, ARM_Y - PIPE_H - NOZZLE_L, AXIS_Z)


def build_spout():
    """The water inlet, fixed: a lead pipe whose end, with a small union, lies on the south face's middle, on
    the axis of a ppex pipe beyond it; from it a lead gooseneck (a run east, a riser, an arm north over the hopper and a short spout down over the
    rocking axis); an oak post on the ground beside the riser and two iron clips round both."""
    h, p = PIPE_H, "spout"
    ax, ay = PIPE_AXIS
    out = [
        box([ax - UNION, ay - UNION, FACE - UNION_T], [ax + UNION, ay + UNION, FACE], "spout_union", p, "pipe"),
        box([ax - h, ay - h, PIPE_Z - h], [ax + h, ay + h, FACE - UNION_T], "spout_inlet", p, "pipe"),
        box([ax + h, PIPE_Y - h, PIPE_Z - h], [SPOUT_X - h, PIPE_Y + h, PIPE_Z + h], "spout_run", p, "pipe"),
        box([SPOUT_X - h, PIPE_Y - h, PIPE_Z - h], [SPOUT_X + h, ARM_Y - h, PIPE_Z + h], "spout_riser", p, "pipe"),
        box([SPOUT_X - h, ARM_Y - h, AXIS_Z - NOZZLE], [SPOUT_X + h, ARM_Y + h, PIPE_Z + h], "spout_arm", p, "pipe"),
        box([SPOUT_X - NOZZLE, ARM_Y - h - NOZZLE_L, AXIS_Z - NOZZLE], [SPOUT_X + NOZZLE, ARM_Y - h, AXIS_Z + NOZZLE], "spout_nozzle", p, "pipe"),
        box([POST_X[0], 0.0, PIPE_Z - h], [POST_X[1], POST_TOP, PIPE_Z + h], "spout_post", p, "oak"),
    ]
    for i, y in enumerate(CLIP_Y):
        out.append(box([SPOUT_X - h - 0.1, y, PIPE_Z - h - 0.07], [POST_X[1] + 0.1, y + CLIP_H, PIPE_Z + h + 0.07], f"spout_clip{i + 1}", p, "iron"))
    return out


def build_stream():
    """The stream from the spout into the hopper while piped water runs: fixed, the hopper rocks under it."""
    mx, my, mz = nozzle_mouth()
    el = box([mx - STREAM_HALF, STREAM_FOOT, mz - STREAM_HALF], [mx + STREAM_HALF, my, mz + STREAM_HALF], "stream_fall", "stream", "water")
    el.render_pass = TRANSPARENT
    return [el]


def build():
    level = build_box() + build_riffles() + build_apron() + build_hopper() + build_charge() + build_concentrate()
    tilt(level)
    return (build_rockers() + level[:4] + build_handle() + level[4:] + build_water() + build_spout() + build_stream()
            + build_sills())


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def rock_drivers():
    """The rock, theta the clock: a swing about the rocking axis, then a slide south by the radius times the
    swing's angle, so the rockers roll on the sills (the running faces' centre moves only along z)."""
    a = ROCK_DEG * DEG
    return [{"type": "swing", "axis": "x", "pivot": pt(8.0, AXIS_Y, AXIS_Z), "amplitude": r6(a), "ratio": 1.0, "phase": 0.0},
            {"type": "slide", "axis": "z", "amplitude": r6(ROCKER_R * a / B), "ratio": 1.0, "phase": 0.0}]


def _rig_parts():
    spec = [
        # (id, glob, requires, ride, drivers)
        ("cradle", "cradle_*", None, None, rock_drivers()),
        ("handle", "handle_*", None, "cradle", []),
        ("riffles", "riffle_*", "riffles", "cradle", []),
        ("apron", "apron_*", "apron", "cradle", []),
        ("hopper", "hopper_*", "hopper", "cradle", []),
        ("riddle", "riddle_*", "riddle", "cradle", []),
        ("water", "water_*", "water", "cradle", []),
        ("charge", "charge_*", "charge", "cradle", []),
        ("concentrate", "conc_*", "concentrate", "cradle", []),
        ("spout", "spout_*", "spout", None, []),
        ("stream", "stream_*", "stream", None, []),
        ("frame", "fr_*", None, None, []),
    ]
    parts = [{"id": i, "match": [g], "requires": r, "ride": ride, "drivers": d} for i, g, r, ride, d in spec]
    for p in parts:
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses
REST = 0.0                                   # theta: the cradle level, the authored pose
MOVING = ("cradle", "handle", "riffles", "apron", "hopper", "riddle", "water", "charge", "concentrate")
STATIC = ("spout", "stream", "frame")


def inputs_of(theta):
    return {"theta": theta}


def pm(parts, pid, theta):
    return _part_matrix(parts, pid, inputs_of(theta))


def sample_thetas(step_deg):
    n = int(round(360 / step_deg))
    return [TAU * i / n for i in range(n)]


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


POINT_ANCHORS = ("spout", "hopper", "outflow", "concentrate", "tailings")


def make_rig(parts):
    (rx0, rx1), (rz0, rz1), ry = riddle_box()
    hopper = tilt_point([(rx0 + rx1) / 2, ry, (rz0 + rz1) / 2])
    lip = tilt_point([BOX_X[0], FLOOR_TOP, AXIS_Z])
    conc = tilt_point([RIFFLE_X[1] + RIFFLE_W + CONC_L / 2, FLOOR_TOP + CONC_H, AXIS_Z])
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, one cell, the controller [0,0,0]: the box runs along x, "
                    "its head (the hopper) east and its open foot west; the operator stands at the handle on the north side, "
                    "and a ppex pipe comes in on the south face. A hand station: no power cell. theta is the rocking, the "
                    "hold-to-work clock: one turn is one rock (over to the south and back over to the north). Points with a "
                    "part ride it. See the rocker's README for the schema.",
        "cells": [],
        "waterCell": [0, 0, 0],
        "waterFace": "south",
        "spout": {"pos": pt(*nozzle_mouth())},
        "hopper": {"pos": pt(*hopper), "part": "cradle"},
        "outflow": {"pos": pt(*lip), "part": "cradle"},
        "concentrate": {"pos": pt(*conc), "part": "cradle"},
        "tailings": {"pos": [-0.5, 0.0, 0.5]},
        "tailingsSide": "west",
        "rock": {"degrees": r6(ROCK_DEG), "radius": r6(ROCKER_R / B),
                 "_comment": "As drawn: the cradle rocks degrees each way, degrees sin(theta), one rock a turn of theta; its "
                             "rockers' running faces have this radius (blocks) and roll on the sills, so it also slides south by "
                             "radius times the angle. It is level at theta 0 and pi: a renderer that stops runs theta on to the "
                             "next of those."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (here no move: the build frame's
    corner is the cell's)."""
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    db = [v / B for v in d]
    ship_els = copy.deepcopy(els)
    translate(ship_els, d)
    ship_parts = copy.deepcopy(parts)
    for p in ship_parts:
        for drv in p["drivers"]:
            for key in ("pivot", "anchor"):
                if key in drv:
                    drv[key] = [r6(drv[key][k] + db[k]) for k in range(3)]
    ship = copy.deepcopy(rig)
    ship["cells"] = [{**c, "pos": shift_cell(c["pos"], ORIGIN_CELL)} for c in rig["cells"]]
    ship["waterCell"] = shift_cell(rig["waterCell"], ORIGIN_CELL)
    for key in POINT_ANCHORS:
        ship[key] = {**rig[key], "pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts):
    """The cell's boxes from the shipped shape as written, posed at rest by the shipped rig; then the lid."""
    written = flatten(shape["elements"], textures={})
    rest = [posed(w, _part_matrix(ship_parts, part_of(ship_parts, w.name), inputs_of(REST))) for w in written]
    by_cell = {}
    for el in rest:
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, []).append(el)
    out = []
    for c in footprint():
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        boxes = cell_boxes(by_cell[pos], pos) if pos in by_cell else None
        out.append({"pos": list(pos), "boxes": boxes} if boxes else {"pos": list(pos), "hollow": True})
    return with_lids(out)


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    poses = [REST, 0.7, TAU / 4, 2.9, 3 * TAU / 4, -1.3]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells {cells}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
REF_THETAS = (0.0, 0.3, TAU / 8, TAU / 4, 2.0, TAU / 2, 4.0, 3 * TAU / 4, 5.5, TAU, -0.7, -2.2, 7.9, 12.0)


def reference_json(ship_parts):
    poses = []
    for th in REF_THETAS:
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(th))) for q in ship_parts}
        poses.append({"theta": r6(th), "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped rocker-rig.json's parts: each part's matrix as 3 rows of 4 "
                        "(block units) at each theta (travel is |theta|; no other input is read). The site's and the mod's "
                        "tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. The charge's and the "
             "concentrate's texture codes ('charge', 'concentrate') are for the renderer to set to the material's; the "
             "water's elements are in the Transparent pass. Keep element names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return (REST, TAU / 4, TAU / 2, 3 * TAU / 4, 0.7, 2.5, 4.4)


def fix_coplanar(els, parts):
    return fix_coplanar_posed(els, lambda es, th: [posed(el, pm(parts, el.part, th)) for el in es], coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the rocker's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_rocker
    els = build()
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        for pose, pairs in before.items():
            print(f"coplanar faces before the fix at theta {pose:.3f}: {len(pairs)} pairs")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_rocker.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "rocker.json", args.out / "rocker_frame.json", args.out / "rocker-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "rocker.json", SHAPE_DIR / "rocker_frame.json", RIG_DIR / "rocker-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_rocker.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship), reference_dumps(reference_json(ship_parts)))
    for path, text in zip(outs, texts):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        json.loads(path.read_text())
        print(f"wrote {path} ({path.stat().st_size // 1024} KiB)")
    if not (check_shipped(els, parts, ship_els, ship_parts, ship) and ok):
        sys.exit(1)
    print("all checks passed")


if __name__ == "__main__":
    main()
