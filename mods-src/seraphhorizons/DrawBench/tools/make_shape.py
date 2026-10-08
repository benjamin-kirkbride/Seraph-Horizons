#!/usr/bin/env python3
"""Generate the draw bench's shapes, rig and reference poses.

The draw bench is a chain draw bench of the kind used from the 1790s to the 1880s to draw lead and
copper tube. A hollow section (the game's chute section, 8 x 8 x 8) goes on the bench threaded on a
square mandrel bar and held against the die by a spring follower. Each stroke draws a quarter of it
through the square die, over the mandrel's plug, into a pipe section (a square tube 6 across, the size
of ppex's pipe, half a block long): the dog (drawing tongs on a portal running on two ways either side
of the section) grips the section's point at the die mouth, and an endless chain beside the bed,
shackled to the dog, hauls it along. The chain's drive sprocket is turned from the vanilla axle
through a rectifier, a cone friction clutch, a two-speed change gear (lead fast, copper slow) and a
final pair. The operator's start lever throws the clutch in and closes the jaws on the point; when the
section's tail leaves the die the jaws spring open and the section drops into the trough under the
bed and slides north down it to queue behind the others; at the end of its travel the dog's lug
knocks the clutch out. A counterweight, lifted during the draw by a rope on a barrel geared to the
return sprocket, falls and hauls the chain, and the dog, back to the die. Four strokes a hollow.
Everything is built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    drawbench.json         the whole machine, every moving part   (assets/.../shapes/block/)
    drawbench_frame.json   the static frame only (block and item)  (assets/.../shapes/block/)
    drawbench-rig.json     cells, anchors and the part rig         (assets/.../config/)
    rig-reference.json     every part's matrix at a grid of poses  (tests/DrawBench/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_drawbench.py) and
exits non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from
the machine box's north-west-bottom corner (the "build frame"); the controller cell is the build
frame's [0,0,0], so the shipped files are the build frame divided by 16. The bench runs along z: the
die end (north, the controller) to the drive head (south).

The rig's inputs, as this machine uses them (README "Rig"):

    theta  the axle angle: the entry shaft, the rectifier's gears
    psi    the axle's travel: the rectified shaft and the clutch's cup
    W      the rig's work, sections drawn: one unit is one stroke's whole cycle; the job ends at 4
    k      the hollow's metal: 0 none, 1 lead (thin), 2 copper (thick)
    p      its presence, 0..1, eased as the hollow is loaded and cleared
    oil    the oil tank's fill, 0..1: the oiler's level
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
from machinegen.geometry import IDENT, TRANSPARENT, El, flatten, rotate, translate  # noqa: E402
from machinegen.output import (reference_dumps, rig_dumps, round_matrix, shape_dumps, shift_cell,  # noqa: E402
                               shift_point, worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, progress_of, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "DrawBench" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/DrawBench/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi
DEG = math.pi / 180

# ---------------------------------------------------------------- the box, the cells
CELLS_X, CELLS_Y, CELLS_Z = 1, 1, 4          # one wide, one high, four long (z)
ORIGIN_CELL = (0, 0, 0)                      # the controller: the die end, nearest the player who placed it
POWER_CELL, POWER_FACE = (0, 0, 3), "west"   # the vanilla axle comes in along x at (y 8, z 56)

TEXTURES = {
    "iron": "game:block/metal/plate/iron",
    "steel": "game:block/metal/sheet-plain/steel1",
    "oak": "game:block/wood/debarked/oak",
    "brass": "game:block/metal/sheet/brass1",
    "cupronickel": "game:block/metal/sheet/cupronickel1",
    "lead": "game:block/metal/sheet-plain/lead1",
    "copper": "game:block/metal/sheet-plain/copper1",
    "leadsheet": "game:block/metal/sheet/lead1",       # the chute section's texture (the game's item: sheet/{metal}1)
    "coppersheet": "game:block/metal/sheet/copper1",
    "die": "game:block/metal/sheet-plain/steel1",   # the renderer sets it to the fitted die's metal
    "rope": "game:item/resource/rope",
    "glass": "game:block/glass/plain",
    "oil": "game:block/liquid/honey",
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the draw line, the stock, the section
DL = (5.0, 11.5)                             # (x, y): the draw line, the section's axis, along z
PIPE = 8.0                                   # a drawn pipe section is half a block long
PIPE_R = 3.0                                 # its half-width (a square tube 6 across, ppex's pipe's size): the die's square bore
WALL = 0.3                                   # its wall; the mandrel's plug is its bore
PLUG_H = PIPE_R - WALL - 0.01
HOLLOW_H, HOLLOW_WALL = 4.0, 1.0             # the hollow section (the game's chute section): 8 across, its wall 1
BAR_H = HOLLOW_H - HOLLOW_WALL - 0.05        # the mandrel bar fills the hollow's bore
SLUGS = 4                                    # a hollow draws into four pipe sections, a quarter of it a stroke
HOLLOW_L = 8.0                               # the hollow section is 8 long
SLUG_L = HOLLOW_L / SLUGS                    # a quarter of it, 2, goes into each section
SEG = 4.0                                    # a section is drawn as two segments, each hidden in the die stock until it emerges
NSEG = int(PIPE / SEG)
POINT = 2.0                                  # the section's point, pushed through the die before its stroke; the jaws bite it

Z_MOUTH = 24.2                               # the die plate's front face: the section emerges here
Z_DIE_BACK, Z_DIE_FRONT = 18.4, 23.4         # the die stock (cast iron, on the ways)
Z_DIE_RING = (Z_DIE_FRONT, Z_MOUTH)
Z_TAIL = (0.3, 1.8)                          # the tail stock: the mandrel bar's clamp
Z_SPRING0 = Z_TAIL[1]
FOLLOWER_T = 0.6
Z_SLUGS0 = Z_DIE_BACK - SLUGS * SLUG_L       # the hollow's far end
Z_FOLLOWER = (Z_SLUGS0 - FOLLOWER_T, Z_SLUGS0)
COILS = 6
Z_PLUG = (Z_MOUTH - 1.4, Z_MOUTH - 0.2)

S_TUBE = PIPE - POINT                        # how far the dog travels while the section is drawn (6)
S_DOG = 8.0                                  # the dog's whole stroke: on past the section's tail to the knock-off
KNOCK = 1.6                                  # the clutch rod's throw: the dog's last 1.6 of travel knocks it out

# ---------------------------------------------------------------- the bed
WAY_TOP = 8.0                                # the ways stand either side of the section, below its middle
WAY_W = (0.2, 1.6)                           # the west way, on its oak beam
WAY_E = (8.4, 9.8)                           # the east way, on its oak beam
BEAM_Y = (1.0, 7.2)                          # the oak beams (full length); the iron ways on them from the die stock on
Z_BED = (0.4, 41.0)
Z_HEAD = (41.0, 58.6)                        # the drive head's bed plate
TRESTLES = ((0.4, 2.4), (39.0, 41.0))
STOCK_X = (0.6, 9.4)
STOCK_TOP = 16.0
STOCK_KEEL = 7.4                             # the die stock reaches down between the ways over the trough

# ---------------------------------------------------------------- the trough: the sections queue in it
TROUGH_X = (WAY_W[1], WAY_E[0])
RAILS_X = ((2.3, 2.9), (7.1, 7.7))
FLOOR0, FLOOR_SLOPE = 0.6, 0.03              # the rails' top: y = FLOOR0 + FLOOR_SLOPE z (it runs down to the north)
FLOOR_ANG = math.atan(FLOOR_SLOPE)
STOP_Z = (0.02, 0.14)                        # the trough's north end
SLOT_GAP = 0.02

# ---------------------------------------------------------------- the dog
DOG_Z = Z_MOUTH + POINT + 0.1                # the dog's back face at rest (the point ends just short of it)
DOG_L = 5.1
SLED_Y = (WAY_TOP, 8.8)                      # its shoes on the ways
CHEEK_DOG = ((WAY_W[0], WAY_W[0] + 0.9), (WAY_E[1] - 0.9, WAY_E[1]))
CROSSBAR_Y = (15.4, 15.95)
TOPPLATE_Y = (15.0, 15.4)
JAW_PIVOT = (DL[0] + PIPE_R + 0.4, Z_MOUTH + 0.5)   # (x, z): the moving (east) jaw's pin, behind its face
JAW_FACE_Z = (Z_MOUTH + 0.7, Z_MOUTH + 1.9)
JAW_Y = (10.0, 13.0)
TAIL_Y = (11.6, 12.4)
TAIL_Z = (JAW_PIVOT[1] - 0.2, JAW_PIVOT[1] + 0.2)
SHANK_X = (10.4, 11.2)

# ---------------------------------------------------------------- the chain and its sprockets
CHAIN_X = 10.8                               # the chain's plane, outside the east beam
SPR_TEETH = 10
SPR_PIN_R = 2.6                              # the pins' circle on a sprocket
PITCH = 2 * SPR_PIN_R * math.sin(math.pi / SPR_TEETH)
R_C = SPR_PIN_R * math.cos(math.pi / SPR_TEETH)   # a link's centre on a sprocket: the chain's kinematic radius
SPR_Y = 4.0                                  # both sprockets' axes
N_Z = 20.0                                   # the return sprocket, under the die stock's east side
REAR_PIN_Z = DOG_Z + 0.6                     # the chain's rear end, shackled to the dog's shank

# ---------------------------------------------------------------- the drive head
ENTRY = (8.0, 56.0)                          # (y, z): the entry shaft, along x from the west face (the power cell's centre)
RECT_MOD = 0.3
RECT_A1, RECT_B1 = 12, 24                    # direct pair: the rectified shaft turns half the axle, opposite
RECT_A2, RECT_I, RECT_B2 = 10, 10, 20        # through the idler: half the axle, the same way
RECT_R = {k: n * RECT_MOD / 2 for k, n in (("a1", RECT_A1), ("b1", RECT_B1), ("a2", RECT_A2), ("i", RECT_I), ("b2", RECT_B2))}
RECT_RATIO = RECT_A1 / RECT_B1               # 1/2
RECT_X1, RECT_X2 = (12.6, 13.2), (13.6, 14.2)   # at the head's east side


def _rect_shaft():
    d = RECT_R["a1"] + RECT_R["b1"]
    dy = 2.4
    return (ENTRY[0] + dy, ENTRY[1] - math.sqrt(d * d - dy * dy))


RECT = _rect_shaft()                         # (y, z) of the rectified shaft (the clutch's and the change gear's)
CG_MOD = 0.25
CG = {"a": 16, "ap": 24, "b": 10, "bp": 30}  # change gears: lead 16:24, copper 10:30 (twice the turns)
CG_R = {k: n * CG_MOD / 2 for k, n in CG.items()}
CG_D = CG_R["a"] + CG_R["ap"]                # 4.5, the same for both pairs
RATIO = {"thin": CG["ap"] / CG["a"], "thick": CG["bp"] / CG["b"]}   # sleeve turns per drive-shaft turn
DRAW_SIGN = 1.0                              # the sleeve and the rectified shaft turn + about x in a draw (sprocket +, drive shaft -, sleeve +)
D_Y = 6.0
D_Z = RECT[1] - math.sqrt(CG_D ** 2 - (RECT[0] - D_Y) ** 2)   # the drive shaft (the change gear's), under the rectified shaft
FD_MOD, FD_TEETH = 0.4, 12                   # the final drive: a pinion on the drive shaft's west end, a wheel on the sprocket shaft, 1:1
FD_R = FD_MOD * FD_TEETH / 2
X_FD = (2.0, 2.6)
S_Z = D_Z - math.sqrt((2 * FD_R) ** 2 - (D_Y - SPR_Y) ** 2)   # the drive sprocket's shaft, north of the drive shaft, at the chain's height
GEAR_W = 0.6
X_AP, X_BP = (4.6, 5.2), (6.9, 7.5)          # the drive shaft's change wheels, east of its final-drive pinion
X_A, X_B = X_AP, (3.5, 4.1)                  # the cluster's pinions, lead position (A in mesh, B free)
SELECT = X_BP[0] - X_B[0]                    # the selector's throw for copper (B to B', A clear east)
X_HUB = (X_B[1], X_A[0])                     # the cluster's hub between its pinions, with the selector's groove
X_CONE = (9.0, 10.3)                         # the cone: a collar, the fork's groove, three steps into the cup
X_CUP = (10.3, 11.4)
CONE_THROW = 0.6
GROOVE_R = 0.85
X_SLEEVE = (3.0, X_CONE[0] + 0.5)
SEL_Y = 14.6                                 # the change gear's selector rod, over the rectifier's big wheel
SEL_X1 = 12.6                                # its east end at rest (lead), through the first guide; copper slides it SELECT further east
SEL_HANDLE_X = (7.6, 8.0)
X_RECT_MID = (11.8, 12.4)                    # the rectified shaft's middle bearing, between the cup and B1
CHEEK_W, CHEEK_E = (0.3, 1.5), (14.65, 15.45)    # the drive head's cheeks (x)
CHEEK_TOP = 11.8
CHEEK_W_Z = (S_Z - 1.4, 57.9)                # it also carries the sprocket shaft's west end
CHEEK_E_Z = (50.0, 57.9)                     # clear of the bell crank's swing
SLEEVE_R = 0.6

# ---------------------------------------------------------------- the return weight
K_Y = SPR_Y + 3.5                            # the barrel shaft, over the return shaft, 1:1
RET_MOD = 0.35
RET_TEETH = 10
RET_R = RET_TEETH * RET_MOD / 2
X_RETGEAR = (11.7, 12.3)
X_STANDARD = (12.35, 12.75)                  # one standard carries the return shaft and the barrel shaft
X_BARREL = (13.0, 14.1)
BARREL_R = 0.75
LIFT = S_DOG / R_C * BARREL_R                # the weight rises this much in a stroke
WEIGHT_X = (12.8, 14.3)
WEIGHT_Y0 = (0.3, 2.6)
WEIGHT_Z = (N_Z + BARREL_R - 0.9, N_Z + BARREL_R + 0.9)
ROPE_Z = N_Z + BARREL_R
ROPE_X = (X_BARREL[0] + X_BARREL[1]) / 2
ROPE_W = 0.3
ROPE_SEGS = 5

# ---------------------------------------------------------------- the controls
LEVER_Y, LEVER_Z = 9.6, Z_MOUTH - 0.8        # the start lever's rock shaft (along x), at the die end
ROD_GUIDE = (45.6, 46.2)                     # the clutch rod's guide at the head; its stop collar rests against it
KNUCKLE_X = (10.4, 10.9)                     # the jaw's tail ends in a knuckle reaching north to the lever's finger
ROD_X, ROD_Y = 15.2, 6.6                     # the clutch rod (along z)
LEVER_ROD_R = LEVER_Y - ROD_Y                # 3.0
LEVER_FINGER_R = (TAIL_Y[0] + TAIL_Y[1]) / 2 - LEVER_Y   # 2.4
LEVER_ANGLE = math.asin(KNOCK / LEVER_ROD_R)
FINGER_TRAVEL = LEVER_FINGER_R * math.sin(LEVER_ANGLE)    # how far the finger closes the jaw's tail


def _jaw_angle():
    """The jaw's turn when the finger pushes its knuckle FINGER_TRAVEL south: the knuckle's contact point
    (on its north face, at the lever's height) moves that far along z as the jaw turns about its pin."""
    x = (KNUCKLE_X[0] + KNUCKLE_X[1]) / 2 - JAW_PIVOT[0]
    z = LEVER_Z + 0.2 - JAW_PIVOT[1]
    lo, hi = 0.0, 1.0
    for _ in range(60):
        a = (lo + hi) / 2
        dz = x * math.sin(a) + z * (math.cos(a) - 1)
        lo, hi = (a, hi) if dz < FINGER_TRAVEL else (lo, a)
    return (lo + hi) / 2


JAW_ANGLE = _jaw_angle()
LUG_EYE_Z = (DOG_Z, DOG_Z + 0.6)             # the dog's knock-off lug: an eye round the rod at the dog's back
S_EAST_BEARING = (11.9, 12.5)                # the drive sprocket's shaft's east bearing
K_EAST_BEARING = (14.45, 15.05)
COLLAR_Z0 = LUG_EYE_Z[1] + (S_DOG - KNOCK) + KNOCK   # the collar's north face with the rod out (rest)
CRANK = (X_CONE[0] + 0.3, RECT[1] - 2.2)     # (x, z) of the bell crank's pin (along y), at the rod's height
CRANK_ROD_R = ROD_X - CRANK[0]
CRANK_FORK_R = RECT[1] - CRANK[1]
CRANK_ANGLE = math.asin(KNOCK / CRANK_ROD_R)

# ---------------------------------------------------------------- the cycle, per section (t = frac(W))
T_START = (0.00, 0.04)                       # the start lever: clutch in, jaws closed on the point
T_DRAW = (0.04, 0.24)                        # the dog travels S_DOG (the rest of the cycle is the return and a dwell)
T_TUBE = T_DRAW[0] + (T_DRAW[1] - T_DRAW[0]) * S_TUBE / S_DOG   # the section's tail leaves the die (0.19)
T_OPEN = (T_TUBE, T_TUBE + 0.04)             # the jaws spring open
T_KNOCK = (T_DRAW[1] - (T_DRAW[1] - T_DRAW[0]) * KNOCK / S_DOG, T_DRAW[1])   # the lug knocks the clutch out
T_DROP = (T_OPEN[1], T_OPEN[1] + 0.03)       # the section drops into the trough
T_ROLL = (T_DROP[1], 0.32)                   # and slides north down it to its place in the queue
T_RETURN = (0.32, 0.52)                      # the weight hauls the dog back (as long as the draw)
T_POINT = (-0.08, 0.0)                       # the next section's point is through the die before its stroke

# ---------------------------------------------------------------- the oiler
OILER = {"x": (9.6, 10.8), "y": (12.6, 13.9), "z": (Z_DIE_BACK + 0.1, Z_DIE_BACK + 1.3)}   # on a bracket off the die stock's east face
OIL_EMPTY = 0.05
OIL_FULL = OILER["y"][1] - OILER["y"][0] - 0.15
SPOUT_Y = (15.6, 15.85)                      # its spout runs back over the die stock's back face, over the hollow
DRIP = (DL[0], HOLLOW_H + DL[1] + 0.05, Z_DIE_BACK - 0.35)


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


AX = {"x": 0, "y": 1, "z": 2}
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}   # a disc's angle 0 direction, and the in-plane direction it turns towards


def frame_of(axis):
    a = AX[axis]
    u, w = REF[axis]
    return a, u, w


def radial(axis, c, a0, a1, r0, r1, width, ang, name, part, tex):
    """A box from radius r0 to r1 about `axis` through c, a0..a1 along the axis, `width` across, at
    angle `ang` (radians, right-handed about +axis from the axis's reference direction)."""
    a, u, w = frame_of(axis)
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
    a, u, w = frame_of(axis)
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


def ring(axis, c, a0, a1, r_in, r_out, n, name, part, tex, odd=0):
    """A tube of n boxes from r_in to r_out (an n-gon outside). Boxes overlap their neighbours and the next
    but one near the bore, so their ends step in by 0, 0.008, 0.016, 0.024 round the ring (no two that
    overlap end in one plane); `odd` turns the steps half round, so a tube laid against another meets it
    with seams of 0.016 to 0.032."""
    w = 2 * r_out * math.tan(math.pi / n)
    out = []
    for i in range(n):
        s = 0.008 * ((i + 2 * odd) % 4)
        out.append(radial(axis, c, a0 + s, a1 - s, r_in, r_out, w, math.pi / n + TAU * i / n, f"{name}{i + 1}", part, tex))
    return out


def gear(axis, c, a0, a1, pitch_r, n, module, name, part, tex, phase=0.0, body_k=4):
    """A spur gear: a polygon body to the root and n teeth of `module` (phase: angle of tooth 1)."""
    width = math.pi * module / 2
    root, tip = pitch_r - 1.25 * module, pitch_r + module
    body = disc(axis, c, a0, a1, root, f"{name}_body", part, tex, k=body_k, phase=phase)
    return body + [radial(axis, c, a0 + 0.03, a1 - 0.03, root - 0.25, tip, width, phase + TAU * i / n, f"{name}_tooth{i + 1}", part, tex)
                   for i in range(n)]


def rod(axis, c, a0, a1, r, name, part, tex, k=4):
    return disc(axis, c, a0, a1, r, name, part, tex, k=k)


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


def mesh_phase(c1, phase1, n1, c2, n2):
    """The tooth phase (angle of tooth 1, about +x, from +y towards +z) for gear 2 meshing gear 1
    (whose tooth 1 is at phase1): on the line of centres, gear 1's tooth and gear 2's gap meet."""
    d = math.atan2(c2[2] - c1[2], c2[1] - c1[1])
    p1, p2 = TAU / n1, TAU / n2
    off = ((d - phase1) / p1) % 1.0
    return (d + math.pi) + p2 / 2 + off * p2


def floor_y(z):
    """The trough rails' top at z."""
    return FLOOR0 + FLOOR_SLOPE * z


def rest_y(z, r=PIPE_R):
    """A section's axis height lying flat on the rails with its middle at z."""
    return floor_y(z) + r / math.cos(FLOOR_ANG)


# The sections queue in the trough from its north end: the first slides furthest, the last stays where it drops.
DROP_Z = Z_MOUTH + PIPE / 2                  # a section's middle as it is drawn (and as it drops)
SLOT_Z = [STOP_Z[1] + PIPE / 2 + SLOT_GAP + (PIPE + SLOT_GAP) * m for m in range(SLUGS)]
assert abs(SLOT_Z[-1] - DROP_Z) < 0.05, SLOT_Z


# ---------------------------------------------------------------- derived positions
ENTRY_C = (0.0, ENTRY[0], ENTRY[1])
RECT_C = (0.0, RECT[0], RECT[1])
D_C = (0.0, D_Y, D_Z)
S_C = (0.0, SPR_Y, S_Z)
N_C = (0.0, SPR_Y, N_Z)
K_C = (0.0, K_Y, N_Z)
DL_C = (DL[0], DL[1], 0.0)


def _idler():
    """The rectifier's idler: RECT_R a2 + i from the entry shaft, i + b2 from the rectified shaft, above."""
    (y1, z1), (y2, z2) = ENTRY, RECT
    r1, r2 = RECT_R["a2"] + RECT_R["i"], RECT_R["i"] + RECT_R["b2"]
    d = math.hypot(y2 - y1, z2 - z1)
    a = (r1 * r1 - r2 * r2 + d * d) / (2 * d)
    h = math.sqrt(r1 * r1 - a * a)
    uy, uz = (y2 - y1) / d, (z2 - z1) / d
    cands = [(y1 + a * uy + s * h * (-uz), z1 + a * uz + s * h * uy) for s in (1.0, -1.0)]
    return max(cands)


IDLER = _idler()
IDLER_C = (0.0, IDLER[0], IDLER[1])


# ---------------------------------------------------------------- builders: the drive head
def build_entry():
    """The entry shaft (oak, the vanilla axle's cross profile, then a round steel shaft) with the
    rectifier's driving gears A1 and A2; B1 and B2 loose on the rectified shaft with their catches,
    the idler on its stud; the rectified shaft."""
    out = []
    y, z = ENTRY
    out.append(box([0.0, y - 0.6, z - 1.5], [3.0, y + 0.6, z + 1.5], "entry_shafta", "entry", "oak"))
    out.append(box([0.0, y - 1.5, z - 0.6], [3.0, y + 1.5, z + 0.6], "entry_shaftb", "entry", "oak"))
    out += rod("x", ENTRY_C, 3.0, CHEEK_E[1] - 0.05, 0.45, "entry_shaft_rod", "entry", "steel")
    out += gear("x", ENTRY_C, *RECT_X1, RECT_R["a1"], RECT_A1, RECT_MOD, "entry_a1", "entry", "steel")
    out += gear("x", ENTRY_C, *RECT_X2, RECT_R["a2"], RECT_A2, RECT_MOD, "entry_a2", "entry", "steel")
    out += gear("x", RECT_C, *RECT_X1, RECT_R["b1"], RECT_B1, RECT_MOD, "rectb1", "rectb1", "steel",
                phase=mesh_phase(ENTRY_C, 0.0, RECT_A1, RECT_C, RECT_B1), body_k=6)
    out.append(box([RECT_X1[1], RECT[0] + 0.55, RECT[1] - 0.25], [RECT_X1[1] + 0.18, RECT[0] + 1.7, RECT[1] + 0.25], "rectb1_catch", "rectb1", "steel"))
    ip = mesh_phase(ENTRY_C, 0.0, RECT_A2, IDLER_C, RECT_I)
    out += gear("x", IDLER_C, *RECT_X2, RECT_R["i"], RECT_I, RECT_MOD, "idler", "idler", "steel", phase=ip)
    out += gear("x", RECT_C, *RECT_X2, RECT_R["b2"], RECT_B2, RECT_MOD, "rectb2", "rectb2", "steel",
                phase=mesh_phase(IDLER_C, ip, RECT_I, RECT_C, RECT_B2), body_k=6)
    out.append(box([RECT_X2[0] - 0.18, RECT[0] - 1.7, RECT[1] - 0.25], [RECT_X2[0], RECT[0] - 0.55, RECT[1] + 0.25], "rectb2_catch", "rectb2", "steel"))
    out += rod("x", RECT_C, CHEEK_W[0] + 0.1, CHEEK_E[1] - 0.05, 0.45, "rectshaft_rod", "rectshaft", "steel")
    out += disc("x", RECT_C, RECT_X1[1], RECT_X2[0], 0.9, "rectshaft_hub", "rectshaft", "steel")
    out += disc("x", RECT_C, X_CUP[1] + 0.2, RECT_X1[0], 0.6, "rectshaft_spacer", "rectshaft", "steel")
    return out


def build_gearbox():
    """The Jonas gearbox's works: the cone clutch's cup (keyed to the rectified shaft), the cone on the
    sleeve (a feather lets it slide), the sleeve loose on the shaft, the change-gear cluster sliding on
    the sleeve, and the drive shaft with its two wheels."""
    out = []
    c = RECT_C
    # the cup: a rim opening west towards the cone, a back plate, a hub
    out += annulus("x", c, X_CUP[0] + 0.05, X_CUP[1] - 0.3, 1.85, 2.4, 12, "cup_rim", "cup", "cupronickel")
    out += disc("x", c, X_CUP[1] - 0.3, X_CUP[1], 2.4, "cup_back", "cup", "cupronickel", k=6)
    out += disc("x", c, X_CUP[1], X_CUP[1] + 0.2, 0.9, "cup_hub", "cup", "cupronickel")
    # the cone: a collar, the fork's groove, then three steps (1.75, 1.5, 1.2) that enter the cup by CONE_THROW
    x0 = X_CONE[0]
    out += disc("x", c, x0, x0 + 0.1, 1.2, "cone_collar", "cone", "steel")
    out += disc("x", c, x0 + 0.1, x0 + 0.5, GROOVE_R, "cone_groove", "cone", "steel")
    st = (X_CONE[1] - x0 - 0.5) / 3
    for i, r in enumerate((1.75, 1.5, 1.2)):
        out += disc("x", c, x0 + 0.5 + st * i, x0 + 0.5 + st * (i + 1), r, f"cone_step{i + 1}", "cone", "cupronickel", k=6)
    # the sleeve, loose on the shaft, with a collar at its west end
    out += rod("x", c, X_SLEEVE[0], X_SLEEVE[1], SLEEVE_R, "sleeve_tube", "sleeve", "steel")
    out += disc("x", c, X_SLEEVE[0] - 0.4, X_SLEEVE[0], 0.9, "sleeve_collar", "sleeve", "steel")
    # the cluster: B (6 teeth, copper), the hub with the selector's groove, A (10 teeth, lead)
    pa = mesh_phase(D_C, 0.0, CG["ap"], c, CG["a"])
    pb = mesh_phase(D_C, 0.0, CG["bp"], c, CG["b"])
    out += gear("x", c, *X_A, CG_R["a"], CG["a"], CG_MOD, "cluster_a", "cluster", "steel", phase=pa)
    out += gear("x", c, *X_B, CG_R["b"], CG["b"], CG_MOD, "cluster_b", "cluster", "steel", phase=pb)
    out += disc("x", c, X_HUB[0], X_HUB[0] + 0.12, 1.1, "cluster_hubw", "cluster", "steel")
    out += disc("x", c, X_HUB[0] + 0.12, X_HUB[1] - 0.12, 0.95, "cluster_groove", "cluster", "steel")
    out += disc("x", c, X_HUB[1] - 0.12, X_HUB[1], 1.1, "cluster_hube", "cluster", "steel")
    # the drive shaft, its change wheels and the final drive's pinion (the sprocket's shaft is the chain's)
    out += rod("x", D_C, CHEEK_W[0] + 0.1, 8.6, 0.45, "driveshaft_rod", "driveshaft", "steel")
    out += gear("x", D_C, *X_FD, FD_R, FD_TEETH, FD_MOD, "driveshaft_fd", "driveshaft", "steel")
    out += gear("x", D_C, *X_AP, CG_R["ap"], CG["ap"], CG_MOD, "driveshaft_ap", "driveshaft", "steel", body_k=6)
    out += gear("x", D_C, *X_BP, CG_R["bp"], CG["bp"], CG_MOD, "driveshaft_bp", "driveshaft", "steel", body_k=6)
    return out


# ---------------------------------------------------------------- the chain
def loop_point(u):
    """The chain's centre line: (y, z, angle about +x of the link's direction from +z), u along the
    loop from the return sprocket's top, round the drive sprocket and back."""
    L = S_Z - N_Z
    arc = math.pi * R_C
    total = 2 * L + 2 * arc
    u %= total
    if u < L:
        return SPR_Y + R_C, N_Z + u, 0.0
    u -= L
    if u < arc:
        a = u / R_C
        return SPR_Y + R_C * math.cos(a), S_Z + R_C * math.sin(a), a
    u -= arc
    if u < L:
        return SPR_Y - R_C, S_Z - u, math.pi
    u -= L
    a = math.pi + u / R_C
    return SPR_Y + R_C * math.cos(a), N_Z + R_C * math.sin(a), a


LOOP = 2 * (S_Z - N_Z) + 2 * math.pi * R_C
U_REAR = REAR_PIN_Z - N_Z
N_LINKS = int((LOOP - 3.5) / PITCH)
PIN_SPAN = LOOP - N_LINKS * PITCH            # the dog's shank spans the chain's two ends
U_FRONT = U_REAR + PIN_SPAN
FRONT_PIN_Z = REAR_PIN_Z + PIN_SPAN


def link_u(i):
    return U_FRONT + (i + 0.5) * PITCH


def segments():
    """The loop's four pieces: (u0, u1, kind, data)."""
    L = S_Z - N_Z
    arc = math.pi * R_C
    return [(0.0, L, "slide", 1.0), (L, L + arc, "turn", S_Z), (L + arc, 2 * L + arc, "slide", -1.0), (2 * L + arc, 2 * L + 2 * arc, "turn", N_Z)]


def link_moves(i):
    """The pieces of the loop link i's centre crosses in a stroke: [(du0, du1, kind, data)], du from
    its rest place; empty pieces dropped."""
    u0 = link_u(i) % LOOP
    out = []
    for base in (0.0, LOOP):
        for a, b, kind, data in segments():
            lo, hi = max(a + base, u0), min(b + base, u0 + S_DOG)
            if hi - lo > 1e-9:
                out.append((lo - u0, hi - u0, kind, data))
    out.sort()
    return out


def link_kind(i):
    moves = link_moves(i)
    if len(moves) == 1 and moves[0][2] == "slide":
        return "top" if moves[0][3] > 0 else "bottom"
    return "own"


def build_chain():
    """The drawing chain: N_LINKS links between the dog's two shackles, alternately outer (wide, low)
    and inner (narrow, deep), each overlapping its neighbours at the pins; the drive sprocket on the
    drive shaft and the return sprocket on the return shaft, ten teeth each, their gaps on the pins."""
    out = []
    for i in range(N_LINKS):
        y, z, a = loop_point(link_u(i))
        kind = link_kind(i)
        part = {"top": "chaintop", "bottom": "chainbottom"}.get(kind, f"ch{i + 1:02d}")
        prefix = {"top": "chaintop", "bottom": "chainbot"}.get(kind, f"ch{i + 1:02d}")
        wide = i % 2 == 0
        hw, hh = (0.5, 0.33) if wide else (0.3, 0.42)
        el = box([CHAIN_X - hw, y - hh, z - PITCH / 2 - 0.12], [CHAIN_X + hw, y + hh, z + PITCH / 2 + 0.12], f"{prefix}_link{i + 1:02d}", part, "iron")
        if abs(a) > 1e-12:
            rotate([el], "x", math.degrees(a), (0.0, y, z))
        out.append(el)
    # the drive sprocket's own shaft, from the west cheek, with the final drive's wheel at its west end
    out += rod("x", S_C, CHEEK_W[0] + 0.1, S_EAST_BEARING[1] - 0.05, 0.45, "drivesprocket_rod", "drivesprocket", "steel")
    out += gear("x", S_C, *X_FD, FD_R, FD_TEETH, FD_MOD, "drivesprocket_fd", "drivesprocket", "steel",
                phase=mesh_phase(D_C, 0.0, FD_TEETH, S_C, FD_TEETH))
    for c, part in ((S_C, "drivesprocket"), (N_C, "returnsprocket")):
        # a tooth between every two pins on this sprocket's arc at rest
        pins = [loop_point(U_FRONT + i * PITCH) for i in range(N_LINKS + 1)]
        on = [math.atan2(z - c[2], y - c[1]) for y, z, _ in pins if math.hypot(y - c[1], z - c[2]) < R_C + 1e-6]
        phase = on[0] + math.pi / SPR_TEETH
        x0, x1 = CHAIN_X - 0.25, CHAIN_X + 0.25
        out += disc("x", c, x0 - 0.15, x1 + 0.15, 1.9, f"{part}_body", part, "iron", k=5, phase=phase)
        for k in range(SPR_TEETH):
            out.append(radial("x", c, x0, x1, 1.6, SPR_PIN_R + 0.35, 0.55, phase + TAU * k / SPR_TEETH, f"{part}_tooth{k + 1}", part, "iron"))
        out += disc("x", c, x1 + 0.15, x1 + 0.6, 0.9, f"{part}_hub", part, "iron")
    return out


# ---------------------------------------------------------------- the return weight
def build_return():
    """The return shaft (from the east beam out to the standard beside the chain) with the
    return sprocket (the chain's) and a gear meshing the barrel shaft's, 1:1; the barrel shaft over it
    with the barrel, the counterweight hung from it by a rope that is taken up into the weight as it
    rises."""
    out = rod("x", N_C, WAY_E[0] + 0.2, X_STANDARD[1] + 0.05, 0.45, "returnshaft_rod", "returnshaft", "steel")
    out += gear("x", N_C, *X_RETGEAR, RET_R, RET_TEETH, RET_MOD, "returnshaft_gear", "returnshaft", "steel")
    out += rod("x", K_C, X_RETGEAR[0] - 0.3, K_EAST_BEARING[1] - 0.05, 0.45, "barrel_rod", "barrel", "steel")
    out += gear("x", K_C, *X_RETGEAR, RET_R, RET_TEETH, RET_MOD, "barrel_gear", "barrel", "steel",
                phase=mesh_phase(N_C, 0.0, RET_TEETH, K_C, RET_TEETH))
    out += disc("x", K_C, X_BARREL[0], X_BARREL[1], BARREL_R, "barrel_drum", "barrel", "oak", k=6)
    for i, (a0, a1) in enumerate(((X_BARREL[0] - 0.2, X_BARREL[0]), (X_BARREL[1], X_BARREL[1] + 0.2)), 1):
        out += disc("x", K_C, a0, a1, BARREL_R + 0.45, f"barrel_flange{i}", "barrel", "iron", k=6)
    # the rope's wraps on the drum
    out += disc("x", K_C, ROPE_X - ROPE_W / 2 - 0.25, ROPE_X + ROPE_W / 2 + 0.25, BARREL_R + 0.12, "barrel_wraps", "barrel", "rope", k=6, phase=0.2)
    (x0, x1), (y0, y1), (z0, z1) = WEIGHT_X, WEIGHT_Y0, WEIGHT_Z
    out.append(box([x0, y0, z0], [x1, y1, z1], "weight_body", "weight", "iron"))
    out.append(box([x0 + 0.15, y1, z0 + 0.15], [x1 - 0.15, y1 + 0.25, z1 - 0.15], "weight_cap", "weight", "iron"))
    out += eye("y", (ROPE_X, 0.0, ROPE_Z), y1 + 0.25, y1 + 0.5, ROPE_W / 2 + 0.02, ROPE_W / 2 + 0.22, "weight_eye", "weight", "iron")
    return out


def rope_segments():
    """The rope from the weight's eye to the barrel: a fixed piece above the weight's highest reach and
    ROPE_SEGS pieces below, each taken up into the weight when it reaches it. (y0, y1, rides from h)."""
    top = WEIGHT_Y0[1] + 0.25                # the weight's cap: the rope goes in through its eye
    fixed = top + LIFT + 0.05
    seg = (fixed - top) / ROPE_SEGS
    return fixed, [(top + seg * i, top + seg * (i + 1)) for i in range(ROPE_SEGS)]


def build_rope():
    fixed, segs = rope_segments()
    out = [box([ROPE_X - ROPE_W / 2, fixed - 0.02, ROPE_Z - ROPE_W / 2], [ROPE_X + ROPE_W / 2, K_Y, ROPE_Z + ROPE_W / 2], "ropetop_piece", "ropetop", "rope")]
    for i, (y0, y1) in enumerate(segs, 1):
        w = ROPE_W / 2 - 0.008 * i
        out.append(box([ROPE_X - w, y0, ROPE_Z - w], [ROPE_X + w, y1 + 0.02, ROPE_Z + w], f"rope{i}_seg", f"rope{i}", "rope"))
    return out


# ---------------------------------------------------------------- the dog
def build_dog():
    """The dog: a portal over the section on the two ways (shoes, cheeks, a crossbar), a top plate
    cantilevered back over the section's point, the fixed (west) jaw hung from it, the moving (east) jaw on
    a pin through it with its tail out east to the start lever's finger and a leaf spring that opens it; out
    east from the east shoe, the shank down to the chain with its two shackle pins and the knock-off lug
    round the clutch rod."""
    d = "dog"
    z0, z1 = DOG_Z, DOG_Z + DOG_L
    (wx0, wx1), (ex0, ex1) = CHEEK_DOG
    out = [box([WAY_W[0], SLED_Y[0], z0], [WAY_W[1], SLED_Y[1], z1], "dog_shoe_w", d, "iron"),
           box([WAY_E[0], SLED_Y[0], z0], [WAY_E[1], SLED_Y[1], z1], "dog_shoe_e", d, "iron"),
           box([wx0, SLED_Y[1], z0], [wx1, CROSSBAR_Y[0], z1], "dog_cheek_w", d, "iron"),
           box([ex0, SLED_Y[1], z0], [ex1, CROSSBAR_Y[0], z1], "dog_cheek_e", d, "iron"),
           box([wx0, CROSSBAR_Y[0], z0], [ex1, CROSSBAR_Y[1], z1], "dog_crossbar", d, "iron"),
           box([DL[0] - PIPE_R - 0.6, TOPPLATE_Y[0], JAW_PIVOT[1] - 0.45], [JAW_PIVOT[0] + 0.5, TOPPLATE_Y[1], z0 + 1.0], "dog_topplate", d, "iron"),
           box([DL[0] - PIPE_R - 0.5, JAW_Y[0], JAW_FACE_Z[0]], [DL[0] - PIPE_R, JAW_Y[1], z0], "dog_jawfixed", d, "steel"),
           box([DL[0] - PIPE_R - 0.5, JAW_Y[1], JAW_FACE_Z[0]], [DL[0] - PIPE_R - 0.1, TOPPLATE_Y[0], z0], "dog_jawhanger", d, "iron")]
    out += rod("y", (JAW_PIVOT[0], 0.0, JAW_PIVOT[1]), JAW_Y[1], TOPPLATE_Y[1] - 0.05, 0.18, "dog_jawpin", d, "steel", k=2)
    # the leaf spring presses the tail towards the die (open), from the east cheek's back face
    out.append(box([ex0 + 0.1, TAIL_Y[0] + 0.1, TAIL_Z[1]], [ex0 + 0.3, TAIL_Y[1] - 0.1, z0], "dog_spring", d, "steel"))
    # out east from the east shoe: a bracket carrying the shank down to the chain, and the knock-off lug
    y = SPR_Y + R_C
    out.append(box([WAY_E[1], SLED_Y[0], REAR_PIN_Z - 0.35], [SHANK_X[1], SLED_Y[1], FRONT_PIN_Z + 0.35], "dog_bracket", d, "iron"))
    out.append(box([SHANK_X[0], y - 0.45, REAR_PIN_Z - 0.35], [SHANK_X[1], SLED_Y[0], FRONT_PIN_Z + 0.35], "dog_shank", d, "iron"))
    for tag, z in (("rear", REAR_PIN_Z), ("front", FRONT_PIN_Z)):
        out += rod("x", (0.0, y, z), SHANK_X[0] - 0.25, SHANK_X[1] + 0.15, 0.18, f"dog_pin{tag}", d, "steel", k=2)
    lz0, lz1 = LUG_EYE_Z
    out.append(box([SHANK_X[1], SLED_Y[0], lz0], [ROD_X - 0.2, SLED_Y[1], lz1], "dog_lugarm", d, "iron"))
    out.append(box([ROD_X - 0.6, ROD_Y + 0.55, lz0], [ROD_X - 0.2, SLED_Y[0], lz1], "dog_lugdrop", d, "iron"))
    out += eye("z", (ROD_X, ROD_Y, 0.0), lz0, lz1, 0.32, 0.6, "dog_lugeye", d, "iron")
    return out


def eye(axis, c, a0, a1, r_in, r_out, name, part, tex):
    """A square eye round an axis: four bars."""
    a, u, w = frame_of(axis)
    out = []
    for i, (du, dw) in enumerate(((1, 0), (-1, 0), (0, 1), (0, -1)), 1):
        lo, hi = [0.0] * 3, [0.0] * 3
        lo[a], hi[a] = a0, a1
        if du:
            lo[u], hi[u] = (c[u] + r_in, c[u] + r_out) if du > 0 else (c[u] - r_out, c[u] - r_in)
            lo[w], hi[w] = c[w] - r_out, c[w] + r_out
        else:
            lo[u], hi[u] = c[u] - r_in, c[u] + r_in
            lo[w], hi[w] = (c[w] + r_in, c[w] + r_out) if dw > 0 else (c[w] - r_out, c[w] - r_in)
        out.append(box(lo, hi, f"{name}{i}", part, tex))
    return out


def build_jaw():
    """The moving jaw: its face beside the point, its boss round the pin, its tail out east."""
    j = "jaw"
    px, pz = JAW_PIVOT
    face = box([DL[0] + PIPE_R, JAW_Y[0], JAW_FACE_Z[0]], [px + 0.1, JAW_Y[1], JAW_FACE_Z[1]], "jaw_face", j, "steel")
    rotate([face], "y", math.degrees(JAW_ANGLE), (px, 0.0, pz))    # drawn open: shut, it lies flat on the point
    out = [face,
           *rod("y", (px, 0.0, pz), JAW_Y[0] + 0.3, JAW_Y[1], 0.35, "jaw_boss", j, "steel"),
           box([px + 0.4, TAIL_Y[0], TAIL_Z[0]], [KNUCKLE_X[1], TAIL_Y[1], TAIL_Z[1]], "jaw_tail", j, "iron"),
           box([KNUCKLE_X[0], TAIL_Y[0], LEVER_Z + 0.2], [KNUCKLE_X[1], TAIL_Y[1], TAIL_Z[0]], "jaw_knuckle", j, "iron")]
    return out


# ---------------------------------------------------------------- the mandrel, follower and spring
def build_mandrel():
    """The mandrel: a square bar that fills the hollow's bore, from the tail stock to its plug in the die's
    throat (the plug is the drawn section's bore); a nut at the tail stock; the follower (a square ring the
    size of the hollow) and its spring (square coils round the bar)."""
    x, y = DL
    out = [box([x - BAR_H, y - BAR_H, Z_TAIL[0] + 0.3], [x + BAR_H, y + BAR_H, Z_PLUG[0]], "mandrel_bar", "mandrel", "steel"),
           box([x - PLUG_H, y - PLUG_H, Z_PLUG[0]], [x + PLUG_H, y + PLUG_H, Z_PLUG[1]], "mandrel_plug", "mandrel", "steel")]
    out += eye("z", DL_C, Z_TAIL[1], Z_TAIL[1] + 0.3, BAR_H, BAR_H + 0.4, "mandrel_nut", "mandrel", "steel")
    (f0, f1) = Z_FOLLOWER
    out += eye("z", DL_C, f0, f1, BAR_H + 0.03, HOLLOW_H - 0.05, "follower_plate", "follower", "iron")
    for i in range(COILS):
        z = coil_z(i)
        out += eye("z", DL_C, z - 0.12, z + 0.12, BAR_H + 0.25, BAR_H + 0.5, f"spring{i + 1}_coil", f"spring{i + 1}", "steel")
    return out


def coil_z(i):
    """Coil i's centre at rest: evenly along the compressed spring, from the tail stock's nut to the follower."""
    a, b = Z_SPRING0 + 0.3, Z_FOLLOWER[0]
    return a + (b - a) * (i + 0.5) / COILS


def coil_share(i):
    a, b = Z_SPRING0 + 0.3, Z_FOLLOWER[0]
    return (coil_z(i) - a) / (b - a)


def build_die():
    c = DL_C
    out = eye("z", c, Z_DIE_RING[0], Z_DIE_RING[1], PIPE_R, DL[1] - WAY_TOP - 0.05, "die_plate", "die", "die")   # a square die, over the ways
    return out


# ---------------------------------------------------------------- the work: slugs and sections
METALS = (("thin", "l", "lead", "billetlead"), ("thick", "c", "copper", "billetcopper"))
SHEET_TEX = {"lead": "leadsheet", "copper": "coppersheet"}   # the hollow (the game's chute section) wears its own sheet


def pipe_r(m, j):
    """Segment j of section m is a hair smaller than the one before it, so hidden ones never share a face."""
    return PIPE_R - 0.012 * j - 0.004 * m


def square_tube(c, z0, z1, h, wall, name, part, tex):
    """A square tube along z, half-width h: top and bottom the full width, the sides between them, as the
    game's chute section is built."""
    x, y = c[0], c[1]
    return [box([x - h, y + h - wall, z0], [x + h, y + h, z1], f"{name}_top", part, tex),
            box([x - h, y - h, z0], [x + h, y - h + wall, z1], f"{name}_bottom", part, tex),
            box([x - h, y - h + wall, z0], [x - h + wall, y + h - wall, z1], f"{name}_west", part, tex),
            box([x + h - wall, y - h + wall, z0], [x + h, y + h - wall, z1], f"{name}_east", part, tex)]


REST_STEP = 0.06                             # hidden segments rest this far apart: their front faces show in an empty bore


def seg_rest(m, j):
    """Pipe m's segment j (j = 0 the point): its front's z at rest, hidden in the die stock. Between strokes
    the die's bore is empty and the frontmost of these faces show in it, so no two lie closer than the
    disc's own 0.012 steps allow (REST_STEP less three of them)."""
    return Z_MOUTH - REST_STEP * (SLUGS * j + m)


def build_work():
    out = []
    c = DL_C
    for cls, pre, tex, req in METALS:
        # the hollow section on the mandrel (the game's chute section, 8 x 8 x 8), in quarters end to end:
        # each goes into the die stock as its pipe section is drawn, so the hollow shortens by a quarter a stroke
        for k in range(SLUGS):
            z1 = Z_DIE_BACK - k * SLUG_L
            out += square_tube(c, z1 - SLUG_L, z1, HOLLOW_H, HOLLOW_WALL, f"{pre}slug{k + 1}_wall", f"{pre}slug{k + 1}", SHEET_TEX[tex])
        for m in range(SLUGS):
            for j in range(NSEG):
                f = seg_rest(m, j)
                part = f"{pre}sect{m + 1}{'ab'[j]}"
                out += square_tube(c, f - SEG, f, pipe_r(m, j), WALL, f"{part}_wall", part, tex)
    return out


# ---------------------------------------------------------------- the controls
def build_controls():
    """The start lever (a rock shaft along x at the die end: a finger up to the jaw's tail, an arm down
    to the clutch rod, the handle), the clutch rod with its knock-off collar and its stop, the bell
    crank at the head with the cone's fork, the change gear's selector rod with its fork and knob."""
    out = []
    # the start lever's rock shaft
    c = (0.0, LEVER_Y, LEVER_Z)
    out += rod("x", c, 10.3, 15.9, 0.3, "startlever_shaft", "startlever", "steel")
    out.append(box([KNUCKLE_X[0], LEVER_Y, LEVER_Z - 0.2], [KNUCKLE_X[1], TAIL_Y[1], LEVER_Z + 0.2], "startlever_finger", "startlever", "iron"))
    out.append(box([ROD_X - 0.25, ROD_Y - 0.15, LEVER_Z - 0.2], [ROD_X + 0.25, LEVER_Y, LEVER_Z + 0.2], "startlever_rodarm", "startlever", "iron"))
    out.append(box([14.6, LEVER_Y, LEVER_Z - 0.2], [15.0, 14.9, LEVER_Z + 0.2], "startlever_handle", "startlever", "iron"))
    out.append(box([14.5, 14.9, LEVER_Z - 0.35], [15.1, 15.6, LEVER_Z + 0.35], "startlever_knob", "startlever", "brass"))
    # the clutch rod: from the lever's arm to the bell crank; the collar the lug strikes; a stop collar at the head's guide
    rc = (ROD_X, ROD_Y, 0.0)
    out += rod("z", rc, LEVER_Z - 0.6, CRANK[1] + 0.5, 0.25, "clutchrod_bar", "clutchrod", "steel", k=2)
    out += disc("z", rc, COLLAR_Z0, COLLAR_Z0 + 0.6, 0.62, "clutchrod_collar", "clutchrod", "steel")
    out += disc("z", rc, ROD_GUIDE[0] - 0.4, ROD_GUIDE[0], 0.62, "clutchrod_stop", "clutchrod", "steel")
    out += rod("x", (0.0, ROD_Y, LEVER_Z), ROD_X - 0.4, ROD_X + 0.25, 0.15, "clutchrod_pin", "clutchrod", "steel", k=2)
    # the bell crank (pin along y at the rod's height): an arm east to the rod, an arm south to the cone's fork
    cx, cz = CRANK
    out += rod("y", (cx, 0.0, cz), ROD_Y - 0.5, ROD_Y + 0.5, 0.35, "crank_boss", "crank", "iron")
    out.append(box([cx + 0.35, ROD_Y - 0.2, cz - 0.25], [ROD_X + 0.35, ROD_Y + 0.2, cz + 0.25], "crank_rodarm", "crank", "iron"))
    out.append(box([cx - 0.25, ROD_Y - 0.2, cz + 0.35], [cx + 0.25, ROD_Y + 0.2, RECT[1] + 0.25], "crank_forkarm", "crank", "iron"))
    out.append(box([cx - 0.15, ROD_Y + 0.2, RECT[1] - 0.25], [cx + 0.15, RECT[0] - GROOVE_R, RECT[1] + 0.25], "crank_fork", "crank", "steel"))
    # the selector: a rod along x over the shafts, its fork down into the cluster's groove, a handle up
    sc = (0.0, SEL_Y, RECT[1])
    gx = (X_HUB[0] + X_HUB[1]) / 2
    out += rod("x", sc, gx - 0.2, SEL_X1, 0.22, "selector_rod", "selector", "steel", k=2)
    out.append(box([gx - 0.12, RECT[0] + 0.95, RECT[1] - 0.3], [gx + 0.12, SEL_Y, RECT[1] + 0.3], "selector_fork", "selector", "steel"))
    out.append(box([SEL_HANDLE_X[0], SEL_Y, RECT[1] - 0.2], [SEL_HANDLE_X[1], 15.0, RECT[1] + 0.2], "selector_handle", "selector", "iron"))
    out.append(box([SEL_HANDLE_X[0] - 0.1, 15.0, RECT[1] - 0.3], [SEL_HANDLE_X[1] + 0.1, 15.6, RECT[1] + 0.3], "selector_knob", "selector", "brass"))
    return out


# ---------------------------------------------------------------- the oiler
def build_oiler():
    """The oiler, the frame's: a sight-feed glass cup on a bracket off the die stock's east face, a brass
    base and cap, and a spout from the cap up and back over the die stock's back face to drip onto the
    hollow ahead of the die; its oil stretches with the rig's `oil`. The glass is in the Transparent
    render pass (an opaque one draws its faint texture solid and hides the oil), so the level shows
    through all four panes."""
    (x0, x1), (y0, y1), (z0, z1) = OILER["x"], OILER["y"], OILER["z"]
    g = 0.15
    xm = (x0 + x1) / 2
    sy0, sy1 = SPOUT_Y
    out = [box([STOCK_X[1], y0 - 0.3, z0], [x0, y1 + 0.3, z1], "fr_oiler_bracket", "frame", "brass"),
           box([x0, y0 - 0.3, z0], [x1, y0, z1], "fr_oiler_base", "frame", "brass"),
           box([x0, y1, z0], [x1, y1 + 0.3, z1], "fr_oiler_cap", "frame", "brass"),
           box([x0, y0, z0], [x1, y1, z0 + g], "fr_oiler_glass_n", "frame", "glass"),
           box([x0, y0, z1 - g], [x1, y1, z1], "fr_oiler_glass_s", "frame", "glass"),
           box([x0, y0, z0 + g], [x0 + g, y1, z1 - g], "fr_oiler_glass_w", "frame", "glass"),
           box([x1 - g, y0, z0 + g], [x1, y1, z1 - g], "fr_oiler_glass_e", "frame", "glass"),
           box([xm - 0.15, y1 + 0.3, (z0 + z1) / 2 - 0.15], [xm + 0.15, sy1, (z0 + z1) / 2 + 0.15], "fr_oiler_riser", "frame", "brass"),
           box([xm - 0.15, sy0, DRIP[2] - 0.15], [xm + 0.15, sy1, (z0 + z1) / 2 - 0.15], "fr_oiler_spoutn", "frame", "brass"),
           box([DRIP[0] - 0.15, sy0, DRIP[2] - 0.15], [xm - 0.15, sy1, DRIP[2] + 0.15], "fr_oiler_spout", "frame", "brass"),
           box([DRIP[0] - 0.12, DRIP[1], DRIP[2] - 0.12], [DRIP[0] + 0.12, sy0, DRIP[2] + 0.12], "fr_oiler_tip", "frame", "brass")]
    out.append(box([x0 + g + 0.02, y0, z0 + g + 0.02], [x1 - g - 0.02, y0 + OIL_EMPTY, z1 - g - 0.02], "oillevel_oil", "oillevel", "oil"))
    for el in out:
        if el.name.startswith("fr_oiler_glass"):
            el.render_pass = TRANSPARENT
    return out


# ---------------------------------------------------------------- the frame
def pedestal(name, axis_c, x0, x1, r_box, floor, tex="iron", post_z=None):
    """A bearing block round a shaft along x at (y, z), on a post down to `floor` (under the block, or
    beside it at post_z = (z0, z1), clear of something under the shaft)."""
    _, y, z = axis_c
    pz = (z - r_box + 0.1, z + r_box - 0.1) if post_z is None else post_z
    top = y - r_box if post_z is None else y + r_box
    return [box([x0, y - r_box, z - r_box], [x1, y + r_box, z + r_box], f"fr_{name}_bearing", "frame", tex),
            box([x0 + 0.05, floor, pz[0]], [x1 - 0.05, top, pz[1]], f"fr_{name}_post", "frame", tex)]


def build_frame():
    f = "frame"
    out = []
    # oak sills on the floor and oak beams on them along both sides; the iron ways on the beams from the
    # die stock on; the trough between the beams, its rails on ties, running down to a stop at the north end
    for tag, (x0, x1) in (("w", WAY_W), ("e", WAY_E)):
        out += [box([max(0.05, x0 - 0.15), 0.0, Z_BED[0]], [x1 + 0.15, BEAM_Y[0], Z_BED[1]], f"fr_sill_{tag}", f, "oak"),
                box([x0, BEAM_Y[0], Z_BED[0]], [x1, BEAM_Y[1], Z_BED[1]], f"fr_beam_{tag}", f, "oak"),
                box([x0, BEAM_Y[1], Z_DIE_FRONT], [x1, WAY_TOP, Z_BED[1]], f"fr_way_{tag}", f, "iron"),
                box([max(x0, STOCK_X[0]), BEAM_Y[1], Z_DIE_BACK], [min(x1, STOCK_X[1]), WAY_TOP, Z_DIE_FRONT], f"fr_diestock_foot{tag}", f, "iron")]
    z_end = Z_MOUTH + PIPE + 0.4
    for i, z in enumerate((1.0, 11.0, 21.0, 31.0), 1):
        out.append(box([WAY_W[1] + 0.15, 0.0, z - 0.4], [WAY_E[0] - 0.15, floor_y(z) - 0.4, z + 0.4], f"fr_tie{i}", f, "oak"))
    for i, (x0, x1) in enumerate(RAILS_X, 1):
        xc = (x0 + x1) / 2
        d = 0.2 / math.cos(FLOOR_ANG)
        out.append(strut([xc, floor_y(STOP_Z[0]) - d, STOP_Z[0]], [xc, floor_y(z_end) - d, z_end], 0.4, x1 - x0, f"fr_rail{i}", f, "iron", axis="x"))
    out.append(box([TROUGH_X[0], floor_y(0.0) - 0.4, STOP_Z[0] - 0.02], [TROUGH_X[1], 3.5, STOP_Z[1]], "fr_trough_stop", f, "iron"))
    # the tail stock and the die stock, across the bed (the die stock on the ways, reaching down between them)
    out += [box([WAY_W[0], BEAM_Y[1], Z_TAIL[0]], [WAY_E[1], 15.3, Z_TAIL[1]], "fr_tailstock", f, "iron"),
            box([STOCK_X[0], WAY_TOP, Z_DIE_BACK], [STOCK_X[1], STOCK_TOP, Z_DIE_FRONT], "fr_diestock", f, "iron"),
            box([TROUGH_X[0], STOCK_KEEL, Z_DIE_BACK], [TROUGH_X[1], WAY_TOP, Z_DIE_FRONT], "fr_diestock_keel", f, "iron")]
    # the drive head: a bed plate, two cast cheeks, the shafts' pedestals, the idler's stud, the crank's post, the rod's guide
    out.append(box([0.2, 0.0, Z_HEAD[0]], [15.8, 1.0, Z_HEAD[1]], "fr_head_bed", f, "iron"))
    out.append(box([CHEEK_W[0], 1.0, CHEEK_W_Z[0]], [CHEEK_W[1], CHEEK_TOP, CHEEK_W_Z[1]], "fr_cheek_w", f, "iron"))
    out.append(box([CHEEK_E[0], 1.0, CHEEK_E_Z[0]], [CHEEK_E[1], CHEEK_TOP, CHEEK_E_Z[1]], "fr_cheek_e", f, "iron"))
    out += pedestal("rect2", RECT_C, *X_RECT_MID, 0.9, 1.0, post_z=(RECT[1] + 0.9, RECT[1] + 1.7))
    out += pedestal("drive2", D_C, 7.9, 8.5, 0.9, 1.0)
    out += pedestal("sprocket", S_C, *S_EAST_BEARING, 0.9, 1.0)
    out.append(box([RECT_X2[1] + 0.3, IDLER[0] - 0.5, IDLER[1] - 0.5], [CHEEK_E[0], IDLER[0] + 0.5, IDLER[1] + 0.5], "fr_idler_arm", f, "iron"))
    out += rod("x", IDLER_C, RECT_X2[0] - 0.2, CHEEK_E[0], 0.3, "fr_idler_stud", f, "steel", k=2)
    cx, cz = CRANK
    out.append(box([cx - 0.3, 1.0, cz - 0.3], [cx + 0.3, ROD_Y - 0.5, cz + 0.3], "fr_crank_post", f, "iron"))
    out += eye("z", (ROD_X, ROD_Y, 0.0), *ROD_GUIDE, 0.27, 0.55, "fr_rod_guide", f, "iron")
    out.append(box([ROD_X - 0.3, 1.0, ROD_GUIDE[0]], [ROD_X + 0.3, ROD_Y - 0.55, ROD_GUIDE[1]], "fr_rod_guidepost", f, "iron"))
    for i, (x0, x1) in enumerate((X_RECT_MID, (CHEEK_E[0] + 0.1, CHEEK_E[1] - 0.1)), 1):
        out += eye("x", (0.0, SEL_Y, RECT[1]), x0, x1, 0.24, 0.5, f"fr_selector_guide{i}_", f, "iron")
        base = RECT[0] + 0.9 if i == 1 else CHEEK_TOP
        out.append(box([x0 + 0.05, base, RECT[1] - 0.3], [x1 - 0.05, SEL_Y - 0.5, RECT[1] + 0.3], f"fr_selector_post{i}", f, "iron"))
    # the start lever's bearings
    for i, (x0, x1) in enumerate(((11.6, 12.2), (15.45, 15.95)), 1):
        out.append(box([x0, LEVER_Y - 0.5, LEVER_Z - 0.5], [x1, LEVER_Y + 0.5, LEVER_Z + 0.5], f"fr_lever_bearing{i}", f, "iron"))
        out.append(box([x0 + 0.05, 0.0, LEVER_Z - 0.4], [x1 - 0.05, LEVER_Y - 0.5, LEVER_Z + 0.4], f"fr_lever_post{i}", f, "iron"))
    # the return shaft runs from the east beam to a standard that also carries the barrel shaft; the barrel
    # shaft's east bearing; both on posts north of the weight
    out.append(box([X_STANDARD[0], SPR_Y - 0.9, N_Z - 0.9], [X_STANDARD[1], K_Y + 0.9, N_Z + 0.9], "fr_standard", f, "iron"))
    out.append(box([X_STANDARD[0] + 0.05, 0.0, N_Z - 1.5], [X_STANDARD[1] - 0.05, K_Y + 0.9, N_Z - 0.85], "fr_standard_post", f, "iron"))
    out.append(box([K_EAST_BEARING[0], K_Y - 0.9, N_Z - 0.9], [K_EAST_BEARING[1], K_Y + 0.9, N_Z + 0.9], "fr_barrel_bearing", f, "iron"))
    out.append(box([K_EAST_BEARING[0] + 0.05, 0.0, N_Z - 1.5], [K_EAST_BEARING[1] - 0.05, K_Y + 0.9, N_Z - 0.85], "fr_barrel_post", f, "iron"))
    out.append(box([X_STANDARD[0], 0.0, N_Z - 1.5], [K_EAST_BEARING[1], 0.4, N_Z - 0.85], "fr_barrel_tie", f, "iron"))
    out.append(box([WEIGHT_X[0] - 0.2, 0.0, WEIGHT_Z[0] - 0.2], [WEIGHT_X[1] + 0.2, WEIGHT_Y0[0], WEIGHT_Z[1] + 0.2], "fr_weight_pad", f, "oak"))
    return out


def build():
    els = build_entry() + build_gearbox() + build_chain() + build_return() + build_rope() + build_dog() + build_jaw()
    els += build_mandrel() + build_die() + build_work() + build_controls() + build_oiler() + build_frame()
    return els


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def per_class(thin, thick=None):
    return {"thin": r6(thin), "thick": r6(thin if thick is None else thick)}


WORK = {"name": "sections drawn", "unit": "sections", "step": 0.005, "end": {"thin": float(SLUGS), "thick": float(SLUGS)}}
PATH = WORK
FOREVER = 1000.0


def windows(r0, r1, f0=None, f1=None):
    """One window per section: rising over r0..r1 of the section's cycle and, given f0..f1 (as long as the
    rise), falling over it; without f0 open to the end of the job."""
    ease = r1 - r0
    if f0 is not None:
        assert abs((f1 - f0) - ease) < 1e-9, (r0, r1, f0, f1)
    return [{"from": r6(m + r0), "to": r6(FOREVER if f1 is None else m + f1), "ease": r6(ease)} for m in range(SLUGS)]


def gauge(motion, axis, amount, wins, pivot=None, mode=None):
    d = {"type": "gauge", "motion": motion, "axis": axis}
    if pivot is not None:
        d["pivot"] = pivot
    d["amount"] = amount
    if mode:
        d["mode"] = mode
    else:
        d["windows"] = wins
    return d


def stroke_window(m, s0, s1):
    """The window over which the dog runs from s0 to s1 of its stroke in section m's cycle, and back."""
    span = T_DRAW[1] - T_DRAW[0]
    w0 = T_DRAW[0] + span * s0 / S_DOG
    e = span * (s1 - s0) / S_DOG
    back = T_RETURN[1] - span * s0 / S_DOG
    return {"from": r6(m + w0), "to": r6(m + back), "ease": r6(e)}


def stroke(s0=0.0, s1=S_DOG):
    return [stroke_window(m, s0, s1) for m in range(SLUGS)]


def once(m, t0, t1):
    """A window in section m's cycle rising over t0..t1 and open to the end of the job."""
    return [{"from": r6(m + t0), "to": FOREVER, "ease": r6(t1 - t0)}]


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def link_drivers(i):
    """A link's gauges: one per piece of the loop it crosses in a stroke, in order."""
    out = []
    for du0, du1, kind, data in link_moves(i):
        if kind == "slide":
            out.append(gauge("slide", "z", per_class(data * (du1 - du0) / B), stroke(du0, du1)))
        else:
            out.append(gauge("rotate", "x", per_class((du1 - du0) / R_C), stroke(du0, du1), pivot=pt(0.0, SPR_Y, data)))
    return out


def _rig_parts():
    turn = S_DOG / R_C                       # the sprockets' turn in a stroke
    sleeve = {"thin": DRAW_SIGN * RATIO["thin"] * turn, "thick": DRAW_SIGN * RATIO["thick"] * turn}
    sleeve_g = gauge("rotate", "x", per_class(sleeve["thin"], sleeve["thick"]), stroke(), pivot=pt(0.0, *RECT))
    clutch_w = windows(T_START[0], T_START[1], T_KNOCK[0], T_KNOCK[1])
    jaw_w = windows(T_START[0], T_START[1], T_OPEN[0], T_OPEN[1])
    parts = [
        {"id": "entry", "match": ["entry_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *ENTRY), "ratio": 1.0}]},
        {"id": "rectb1", "match": ["rectb1_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *RECT), "ratio": r6(-RECT_RATIO)}]},
        {"id": "idler", "match": ["idler_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *IDLER), "ratio": r6(-RECT_A2 / RECT_I)}]},
        {"id": "rectb2", "match": ["rectb2_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *RECT), "ratio": r6(RECT_RATIO)}]},
        # the rectified shaft turns the way the sleeve turns in a draw (negative about x), whichever way the axle does
        {"id": "rectshaft", "match": ["rectshaft_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *RECT), "ratio": r6(DRAW_SIGN * RECT_RATIO), "input": "travel"}]},
        {"id": "cup", "match": ["cup_*"], "requires": "gearbox",
         "drivers": [{"type": "rotate", "axis": "x", "pivot": pt(0.0, *RECT), "ratio": r6(DRAW_SIGN * RECT_RATIO), "input": "travel"}]},
        {"id": "cone", "match": ["cone_*"], "requires": "gearbox",
         "drivers": [sleeve_g, gauge("slide", "x", per_class(CONE_THROW / B), clutch_w)]},
        {"id": "sleeve", "match": ["sleeve_*"], "requires": "gearbox", "drivers": [sleeve_g]},
        {"id": "cluster", "match": ["cluster_*"], "requires": "gearbox",
         "drivers": [sleeve_g, gauge("slide", "x", per_class(0.0, SELECT / B), None, mode="present")]},
        {"id": "driveshaft", "match": ["driveshaft_*"], "requires": "gearbox",
         "drivers": [gauge("rotate", "x", per_class(-turn), stroke(), pivot=pt(0.0, D_Y, D_Z))]},
        {"id": "selector", "match": ["selector_*"], "requires": None,
         "drivers": [gauge("slide", "x", per_class(0.0, SELECT / B), None, mode="present")]},
        {"id": "startlever", "match": ["startlever_*"], "requires": None,
         "drivers": [gauge("rotate", "x", per_class(LEVER_ANGLE), clutch_w, pivot=pt(0.0, LEVER_Y, LEVER_Z))]},
        {"id": "clutchrod", "match": ["clutchrod_*"], "requires": None,
         "drivers": [gauge("slide", "z", per_class(-KNOCK / B), clutch_w)]},
        {"id": "crank", "match": ["crank_*"], "requires": None,
         "drivers": [gauge("rotate", "y", per_class(CRANK_ANGLE), clutch_w, pivot=pt(CRANK[0], 0.0, CRANK[1]))]},
        {"id": "drivesprocket", "match": ["drivesprocket_*"], "requires": "chain",
         "drivers": [gauge("rotate", "x", per_class(turn), stroke(), pivot=pt(0.0, SPR_Y, S_Z))]},
        {"id": "returnsprocket", "match": ["returnsprocket_*"], "requires": "chain",
         "drivers": [gauge("rotate", "x", per_class(turn), stroke(), pivot=pt(0.0, SPR_Y, N_Z))]},
        {"id": "chaintop", "match": ["chaintop_*"], "requires": "chain",
         "drivers": [gauge("slide", "z", per_class(S_DOG / B), stroke())]},
        {"id": "chainbottom", "match": ["chainbot_*"], "requires": "chain",
         "drivers": [gauge("slide", "z", per_class(-S_DOG / B), stroke())]},
    ]
    for i in range(N_LINKS):
        if link_kind(i) == "own":
            parts.append({"id": f"ch{i + 1:02d}", "match": [f"ch{i + 1:02d}_*"], "requires": "chain", "drivers": link_drivers(i)})
    parts += [
        {"id": "returnshaft", "match": ["returnshaft_*"], "requires": None,
         "drivers": [gauge("rotate", "x", per_class(turn), stroke(), pivot=pt(0.0, SPR_Y, N_Z))]},
        {"id": "barrel", "match": ["barrel_*"], "requires": None,
         "drivers": [gauge("rotate", "x", per_class(-turn), stroke(), pivot=pt(0.0, K_Y, N_Z))]},
        {"id": "weight", "match": ["weight_*"], "requires": None,
         "drivers": [gauge("slide", "y", per_class(LIFT / B), stroke())]},
    ]
    top = WEIGHT_Y0[1] + 0.25
    _, segs = rope_segments()
    for i, (y0, y1) in enumerate(segs, 1):
        h0 = y1 - top + 0.05                 # the weight has risen this far when it takes this piece up
        if h0 < LIFT:
            s0 = h0 / LIFT * S_DOG
            parts.append({"id": f"rope{i}", "match": [f"rope{i}_*"], "requires": None,
                          "drivers": [gauge("slide", "y", per_class((LIFT - h0) / B), stroke(s0, S_DOG))]})
        else:
            parts.append({"id": f"rope{i}", "match": [f"rope{i}_*"], "requires": None, "drivers": []})
    jp = pt(JAW_PIVOT[0], 0.0, JAW_PIVOT[1])
    parts += [
        {"id": "dog", "match": ["dog_*"], "requires": "dog",
         "drivers": [gauge("slide", "z", per_class(S_DOG / B), stroke())]},
        {"id": "jaw", "match": ["jaw_*"], "requires": "dog", "ride": "dog",
         "drivers": [gauge("rotate", "y", per_class(-JAW_ANGLE), jaw_w, pivot=jp)]},
        {"id": "mandrel", "match": ["mandrel_*"], "requires": "mandrel", "drivers": []},
        {"id": "follower", "match": ["follower_*"], "requires": "mandrel",
         "drivers": [gauge("slide", "z", per_class(SLUG_L / B), once(m, T_DRAW[0], T_TUBE)) for m in range(SLUGS)]},
    ]
    for i in range(COILS):
        parts.append({"id": f"spring{i + 1}", "match": [f"spring{i + 1}_*"], "requires": "mandrel",
                      "drivers": [gauge("slide", "z", per_class(SLUG_L * coil_share(i) / B), once(m, T_DRAW[0], T_TUBE)) for m in range(SLUGS)]})
    parts.append({"id": "die", "match": ["die_*"], "requires": "die", "drivers": []})
    span = T_DRAW[1] - T_DRAW[0]
    for cls, pre, tex, req in METALS:
        for k in range(SLUGS):
            parts.append({"id": f"{pre}slug{k + 1}", "match": [f"{pre}slug{k + 1}_*"], "requires": req,
                          "drivers": [gauge("slide", "z", per_class(SLUG_L / B), once(m, T_DRAW[0], T_TUBE)) for m in range(k + 1)]})
        for m in range(SLUGS):
            dz, dy = SLOT_Z[m] - DROP_Z, rest_y(SLOT_Z[m]) - rest_y(DROP_Z)
            for j in range(NSEG):
                part = f"{pre}sect{m + 1}{'ab'[j]}"
                drivers = []
                if j == 0:
                    # every point comes to the same place in the jaws, from however deep it rested
                    drivers.append(gauge("slide", "z", per_class((Z_MOUTH + POINT - seg_rest(m, 0)) / B), once(m, *T_POINT)))
                    start = 0.0
                else:
                    start = seg_rest(m, j) + SEG * j - (Z_MOUTH + POINT)
                drivers.append(gauge("slide", "z", per_class((S_TUBE - start) / B),
                                     once(m, T_DRAW[0] + span * start / S_DOG, T_TUBE)))
                # it drops into the trough, tipping to lie flat on its rails, then slides north down them
                drivers.append(gauge("rotate", "x", per_class(-FLOOR_ANG), once(m, *T_DROP), pivot=pt(0.0, DL[1], DROP_Z)))
                drivers.append(gauge("slide", "y", per_class((rest_y(DROP_Z) - DL[1]) / B), once(m, *T_DROP)))
                if abs(dz) > 1e-9:
                    drivers.append(gauge("slide", "z", per_class(dz / B), once(m, *T_ROLL)))
                    drivers.append(gauge("slide", "y", per_class(dy / B), once(m, *T_ROLL)))
                parts.append({"id": part, "match": [f"{part}_*"], "requires": req, "drivers": drivers})
    (ox0, ox1), (oy0, oy1), (oz0, oz1) = OILER["x"], OILER["y"], OILER["z"]
    parts += [
        {"id": "oillevel", "match": ["oillevel_*"], "requires": None,
         "drivers": [{"type": "stretch", "axis": "y", "anchor": pt((ox0 + ox1) / 2, oy0, (oz0 + oz1) / 2),
                      "length": r6(OIL_EMPTY / B), "travel": r6((OIL_FULL - OIL_EMPTY) / B), "input": "oil"}]},
        {"id": "ropetop", "match": ["ropetop_*"], "requires": None, "drivers": []},
        {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []},
    ]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses
REST = (0.0, 0.0, 0.0, 0, 0.0, 0.0)          # (theta, travel, W, k, p, oil): no hollow, the tank empty; the authored pose


def inputs_of(pose):
    th, ps, W, k, p = pose[:5]
    oil = pose[5] if len(pose) > 5 else 0.0
    return {"theta": th, "travel": ps, "work": W, "size": k, "presence": p, "oil": oil}


def pm(parts, pid, pose, path=None):
    return _part_matrix(parts, pid, inputs_of(pose), path or PATH)


def turns_per_section(cls):
    """Axle turns per section's cycle, as drawn: the sleeve turns RATIO x the sprocket's S_DOG / R_C in a
    stroke, the rectified shaft half the axle's travel, and the stroke is T_DRAW of the cycle."""
    return RATIO[cls] * (S_DOG / R_C) / RECT_RATIO / (T_DRAW[1] - T_DRAW[0]) / TAU


def pose_at(k, W, theta=None, oil=0.6):
    """A pose of a job in progress: metal k, at W; the axle at its drawn pace."""
    th = TAU * turns_per_section(("thin", "thick")[k - 1]) * W if theta is None else theta
    return (th, abs(th), W, k, 1.0, oil)


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("output", "die", "drip")


def make_rig(parts):
    progress_of({"work": WORK})
    return {
        "_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the die end, nearest the "
                    "player who placed it; the bench runs south to the drive head. work is the job's progress, W, in sections "
                    "drawn (a unit is one stroke's whole cycle; 4 a hollow section); k is the hollow's metal (1 lead, 2 copper); gauge "
                    "windows are placed in sections. The oiler's level follows the rig's oil input (the MachineOil tank's fill, "
                    "0..1). See the draw bench's README for the schema.",
        "cells": [],
        "powerCell": list(POWER_CELL),
        "powerFace": POWER_FACE,
        "infeedSide": "north",
        "outputSide": "east",
        "output": {"pos": pt(CELLS_X * B - 0.01, rest_y(SLOT_Z[0]), SLOT_Z[0])},
        "die": {"pos": pt(DL[0], DL[1], Z_MOUTH)},
        "drip": {"pos": pt(*DRIP)},
        "work": dict(WORK),
        "draw": {"turnsPerSection": per_class(turns_per_section("thin"), turns_per_section("thick")),
                 "sectionsPerHollow": SLUGS,
                 "hollows": {"thin": "game:chutesection-lead", "thick": "game:chutesection-copper"},
                 "_comment": f"turnsPerSection: axle turns per section's cycle as the gearing is drawn (the rectifier's {RECT_A1}:{RECT_B1}, "
                             f"the change gear's {CG['a']}:{CG['ap']} for lead and {CG['b']}:{CG['bp']} for copper, the drive "
                             f"sprocket's turn over a stroke that is {T_DRAW[1] - T_DRAW[0]:g} of the cycle). sectionsPerHollow: the "
                             "job's end (each a seraphhorizons:pipesection of the hollow's metal). hollows: what each class's work is "
                             "(the game's chute section, drawn over the mandrel)."},
        "parts": parts,
    }


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0] (here no move: the build frame's
    corner is the controller's). Gauge windows are in sections, so they stay as they are."""
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
    ship["powerCell"] = shift_cell(rig["powerCell"], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def shipped_cells(shape, ship_parts, sp):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig (every reader
    rebuilds them the same way); then the lids."""
    written = flatten(shape["elements"], textures={})
    rest = [posed(w, _part_matrix(ship_parts, part_of(ship_parts, w.name), inputs_of(REST), sp)) for w in written]
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
    sp = ship["work"]
    poses = [REST, pose_at(1, 0.3), pose_at(1, 1.47, oil=1.0), pose_at(2, 0.55), pose_at(2, 2.9, oil=0.2),
             (1.3, 7.0, 1.2, 1, 0.6, 0.3), (0.4, 2.0, 3.0, 2, 1.0, 1.0)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose), sp), d, poses)
    cells = [tuple(c["pos"]) for c in ship["cells"]]
    span = [(min(c[k] for c in cells), max(c[k] for c in cells)) for k in range(3)]
    print(f"shipped: moved by {[v / B for v in d]} blocks, worst posed difference {worst:.2e} voxels; cells x {span[0]}, y {span[1]}, "
          f"z {span[2]}; power {ship['powerCell']} {ship['powerFace']}")
    if worst > 1e-6 or (0, 0, 0) not in cells:
        print("FAIL: the shipped model is not the checked one moved")
        return False
    return True


# ---------------------------------------------------------------- reference poses
def reference_poses():
    """theta in {0, 1.1, -2.3, 2.9} with psi; for each metal, W over the cycle's edges in the first,
    second and last section, W at 0 and 4, p at 1 and 0.4; no hollow; the oil at 0, 0.35 and 1 in turn."""
    out = []
    for i, th in enumerate((0.0, 1.1, -2.3, 2.9)):
        for extra in (0.0, 7.3):
            out.append((th, round(abs(th) + extra, 6), 0.0, 0, 0.0, (0.0, 0.35, 1.0)[i % 3]))
    edges = (-0.04, 0.02, 0.05, 0.13, 0.24, 0.33, 0.385, 0.395, 0.41, 0.425, 0.435, 0.445, 0.47, 0.49, 0.515, 0.55, 0.71, 0.88, 0.93, 0.97)
    for k in (1, 2):
        ws = {0.0, float(SLUGS)}
        for m, es in ((0, edges), (1, edges[::2]), (SLUGS - 1, edges[1::2])):
            for e in es:
                if m + e >= 0:
                    ws.add(round(m + e, 6))
        for i, W in enumerate(sorted(ws)):
            th = (0.0, 1.1, -2.3, 2.9)[i % 4]
            for p in ((1.0,) if i % 5 else (1.0, 0.4)):
                out.append((th, round(abs(th) + 0.37 * i, 6), W, k, p, (1.0, 0.35, 0.0)[i % 3]))
    return out


def reference_json(ship_parts, sp):
    poses = []
    for pose in reference_poses():
        th, ps, W, k, p, oil = pose
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(pose), sp)) for q in ship_parts}
        poses.append({"theta": th, "travel": ps, "work": W, "size": k, "presence": p, "oil": oil, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped drawbench-rig.json's parts and work: each part's matrix as 3 "
                        "rows of 4 (block units) at each pose (W in sections). The site's and the mod's tests check their rig maths "
                        "against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. The die's texture code is 'die': "
             "the renderer sets it to the fitted die's metal. Keep element names when editing: the rig finds its parts by them.",
        TEXTURES, tex_size=TEX)


def coplanar_poses():
    return (REST, pose_at(1, 0.25), pose_at(2, 1.25), pose_at(1, 2.6), pose_at(2, 3.0))


HIDER = ([STOCK_X[0], STOCK_KEEL, Z_DIE_BACK], [STOCK_X[1], STOCK_TOP, Z_MOUTH + 1e-3])   # the die stock and its ring


def on_show(part, k):
    """Whether a part can be seen with metal k on the bench: each metal's work only with that metal."""
    if part[:5] in ("lslug", "lsect"):
        return k == 1
    if part[:5] in ("cslug", "csect"):
        return k == 2
    return True


def shown(posed_els, pose):
    """The posed elements as they can be seen: another metal's work, the work hidden in the die stock and
    the rope taken up into the weight moved far away (copies; the order kept), so the z-fighting fix and
    its check deal only with faces that show."""
    k = pose[3]
    wb = [e.aabb() for e in posed_els if e.name in ("weight_body", "weight_cap")]
    wlo = [min(b[0][q] for b in wb) for q in range(3)] if wb else [0.0] * 3
    whi = [max(b[1][q] for b in wb) for q in range(3)] if wb else [0.0] * 3
    out = []
    for e in posed_els:
        lo, hi = e.aabb()
        gone = not on_show(e.part, k)
        if e.part[:5] in ("lslug", "lsect", "cslug", "csect"):
            gone = gone or all(HIDER[0][q] - 1e-6 <= lo[q] and hi[q] <= HIDER[1][q] + 1e-6 for q in range(3))
        if e.part.startswith("rope") and e.part != "ropetop":
            gone = gone or all(wlo[q] - 1e-6 <= lo[q] and hi[q] <= whi[q] + 1e-6 for q in range(3))
        if gone:
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def fix_coplanar(els, parts):
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose), coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the draw bench's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_drawbench
    els = build()
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
    parts = rig_parts()
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        for pose, pairs in before.items():
            print(f"coplanar faces before the fix at {pose}: {len(pairs)} pairs")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    rig = make_rig(parts)
    ok = validate_drawbench.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "drawbench.json", args.out / "drawbench_frame.json", args.out / "drawbench-rig.json", args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "drawbench.json", SHAPE_DIR / "drawbench_frame.json", RIG_DIR / "drawbench-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts, ship["work"])
    ok = validate_drawbench.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
    texts = (shape_dumps(shape), shape_dumps(frame_shape), rig_dumps(ship), reference_dumps(reference_json(ship_parts, ship["work"])))
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
