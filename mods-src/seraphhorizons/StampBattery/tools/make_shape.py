#!/usr/bin/env python3
"""Generate the stamp battery's shapes, rig and reference poses.

The stamp battery crushes and classifies in one machine (#728, model #729): a wet five-stamp gravity battery
with its mesh discharge screen, and at level 2 the Challenge self-feeder. One machine is one piece of equipment
(#711): a timber battery frame whose footprint is fixed from the frame on, and the parts each level fits into it.

    frame    two oak end frames (sill, front, middle and back posts, cap, the guide and cam rails), the guide
             girts, the ties and the hopper's two bearers; the power train: the entry shaft from the vanilla axle
             into a closed rectifier gear case, and the line shaft along the back row
    level 1  the battery: the mortar box on its block, with its dies, the mesh screen in its front wall, the
             apron and the launder along the front, the output chute section flush with the front face, the
             shoot from the feed opening, and the infeed chute section in the top face (`mortar`); the camshaft
             with ten involute cams and the bull pulley, belted from the line shaft (`camshaft`); five stamps
             (`stamps`); the plank feed box behind the mortar under the infeed (`feedbox`); the water line from
             the west face into the feed opening, ppex's straight pipe soldered in copper or lead (`pipecopper`,
             `pipelead`)
    level 2  the Challenge feeder in the feed box's place (`feeder`): a sheet-iron hopper with an inclined
             floor on the two bearers, a round tray under it on a vertical spindle, turned through teeth in its
             bottom by a pinion whose shaft carries the friction grip; the grip is worked by the lever E and the
             bumper rod G, which the feeding tappet on stamp 3 strikes each time the stamp drops low. Fitting the
             feeder takes the feed box off.

Everything is built here from plain boxes; no other mod's model is used. It writes, deterministically,

    stampbattery.json         the whole machine, every level's parts      (assets/.../shapes/block/)
    stampbattery_frame.json   the static frame only (block and item)      (assets/.../shapes/block/)
    stampbattery-rig.json     cells, anchors and the part rig             (assets/.../config/)
    rig-reference.json        every part's matrix at a grid of poses      (tests/StampBattery/)

or, with `--out DIR`, all four into DIR. It validates its own output (validate_stampbattery.py) and exits
non-zero if a check fails.

Everything is in voxels in the native frame (x west to east, y up, z north to south), measured from the machine
box's north-west bottom corner (the "build frame"); the controller cell is the bottom middle cell of the north
face, ORIGIN_CELL, so the shipped files are the build frame moved by -ORIGIN_CELL blocks. The machine is placed
with its north face (the screen, the launder and the output) towards the player, extending away from them.

The rig's inputs: theta, the axle's angle (the entry shaft), and psi, its travel either way, which everything
behind the rectifier follows: the line shaft, the camshaft, the stamps (each a sum of harmonics of the camshaft's
turn: lifted by its cam, thrown, falling and resting on its die) and the feeder (its bumper rod and lever follow
stamp 3's drop; the grip, the pinion and the tray turn a step at each strike, a steady turn plus harmonics).
The rig has no work.
"""

from __future__ import annotations

import argparse
import copy
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Machines" / "tools"))
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Pipes" / "tools"))
sys.path.insert(0, str(Path(__file__).resolve().parent))

import solderedpipe  # noqa: E402

from machinegen.checks import cell_boxes, cells_touched, with_lids  # noqa: E402
from machinegen.checks import fix_coplanar as fix_coplanar_posed  # noqa: E402
from machinegen.geometry import IDENT, El, flatten, rotate, translate  # noqa: E402
from machinegen.output import (reference_dumps, rig_dumps, round_matrix, shape_dumps, shift_cell,  # noqa: E402
                               shift_point, worst_shift_error)
from machinegen.output import shape_json as machine_shape_json  # noqa: E402
from machinegen.rigmath import part_of, posed, validate_driver  # noqa: E402
from machinegen.rigmath import part_matrix as _part_matrix  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "block"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
REFERENCE_OUT = MOD / "tests" / "StampBattery" / "rig-reference.json"
SCRIPT = "mods-src/seraphhorizons/StampBattery/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi

# ---------------------------------------------------------------- the box, the cells, the anchors
# Three deep: the front row (z 0..16) holds the apron, the launder and the output chute; the middle row (z 16..32)
# the mortar, the stamps and the camshaft; the back row (z 32..48) the feed box or the feeder under the infeed, the
# line shaft and the gear case.
CELLS_X, CELLS_Y, CELLS_Z = 3, 4, 3          # fixed from the frame on
ORIGIN_CELL = (1, 0, 0)                      # the controller: the bottom middle cell of the north face, the output's
POWER_CELL, POWER_FACE = (2, 0, 2), "east"   # the vanilla axle, along x at (y 8, z 40), into the gear case
WATER_CELL, WATER_FACE = (0, 2, 2), "west"   # the ppex pipe, along x at (y 40, z 40), the water line to the feed opening
INFEED_CELL, INFEED_FACE = (1, 3, 2), "up"   # the infeed chute's mouth: ore by chute from above, or by hand, at both levels
OUTPUT_CELL, OUTPUT_FACE = (1, 0, 0), "north"   # the output chute's mouth, flush with the front face

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "planks": "game:block/wood/planks/oak1",
    "iron": "game:block/metal/plate/iron",
    "sheet": "game:block/metal/sheet/iron1",  # the feeder's sheet-iron hopper (Rose 1898, p. 122)
    "steel": "game:block/metal/sheet-plain/steel1",
    # the water line wears ppex's pipe textures as UnifiedPipes gives them, as the rosser's drip pipes do
    **{f"pipe{m}": f"game:block/metal/sheet-plain/{m}4" for m in ("copper", "lead")},
    "solder": solderedpipe.SOLDER_TEXTURE,
    "mesh": "game:block/metal/mesh1",
    "chute": "game:block/metal/sheet/copper1",   # the game's chute; the pack leaves its texture as the game's (copper)
    "leather": "game:block/leather/plain",
}
TEX = 64                                     # shape texture units; 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the frame (oak): two end frames and their ties
END_W, END_E = (3.5, 7.5), (40.5, 44.5)      # the end frames (x): outside the mortar's ends
SILL_Z = (9.0, 34.0)                         # the sills: under the front and middle posts
CAP_Z = (9.0, 47.6)                          # the caps: over all three posts
POST_F, POST_M, POST_B = (11.0, 15.0), (30.0, 34.0), (43.6, 47.6)   # front, middle (behind the mortar) and back posts (z)
SILL_Y, CAP_Y = (0.0, 4.0), (60.0, 64.0)
RAIL_IN = 0.5                                # rails stand in from the posts' faces
RAIL_LO_Y = (19.0, 23.0)                     # carries the lower guide girts
RAIL_HI_Y = (42.0, 46.0)                     # carries the upper guide girts
GUIDE_H = 3.0                                # the guide girts' height over their rails
TIE_C_Z = (32.0, 36.0)                       # the chute tie at the caps, against the infeed chute's north wall
TIE_B_Z = (44.0, POST_B[1])                  # the back tie at the caps, against its south wall
BEARER_Y = (44.0, 48.0)                      # the hopper's two bearers, between the middle and the back posts
PLANK = 1.0                                  # board thickness: launder, feed box, guides' strips

# ---------------------------------------------------------------- level 1: the stamps
STAMPS = 5
STEM_X0, STEM_PITCH = 12.4, 5.8              # the stems' x
STEM_Z = 24.0                                # the stems' line (z): the middle row's
STEM_R = 0.8                                 # octagons, by apothem
SHOE_R, HEAD_R = 2.3, 2.4
DIE_TOP = 8.5                                # the dies' top: the shoes rest on them
SHOE_T, HEAD_T = 4.0, 4.5
STEM_TOP = 54.0                              # at rest
TAPPET_H, TAPPET_T = 2.2, 4.0                # the tappet: a square collar 4.4 across, 4 high
LIFT = 4.0                                   # each stamp's lift on its cam
ORDER = (1, 3, 5, 2, 4)                      # the firing order: ten drops a camshaft turn, evenly spaced

# the cams: involutes of a base circle CAM_RB, beside each stem, lifting the tappet's north edge
CAM_RB = 3.5                                 # the cams' base circle: the involute lifts the tappet CAM_RB a radian
CAM_T0 = 0.4                                 # the involute's roll at pick-up: the tappet rests CAM_RB * CAM_T0 over the camshaft
CAM_T1 = CAM_T0 + LIFT / CAM_RB              # its roll at the tip, where the tappet is let go
CAM_EDGE = 0.25                              # the contact line stands this far inside the tappet's north edge
CAM_X = (1.0, 2.1)                           # each cam's plate east of its stem's axis: it sweeps past the stem
CAM_ARM = 1.2                                # an arm's depth behind its working face
CAM_HUB_R = 2.6
CAM_SEGS = 4                                 # chords an arm
CAM_ROOT = 0.25                              # an arm's working face starts at this roll; a web joins its root to the hub
CAM_Y = 34.0
CAM_Z = STEM_Z - (TAPPET_H - CAM_EDGE + CAM_RB)   # the camshaft, north of the stems
CAM_C = (CAM_Y, CAM_Z)
CAM_TIP = CAM_RB * math.hypot(1.0, CAM_T1)   # the cams' reach from the camshaft
TAPPET_Y0 = CAM_Y + CAM_RB * CAM_T0          # the tappet's underside, the stamp resting on its die
SHAFT_R = 1.5

# the stamp's lift through a period (half a camshaft turn: double cams): the cam's rise, then thrown and falling
# freely onto its die (FALL radians of the camshaft), then resting; smoothed by a Gaussian of SMOOTH radians (the
# cam's pick-up and the shoe's landing on the ore bed), drawn by its first HARMONICS harmonics
FALL = 0.9
SMOOTH = 0.06
HARMONICS = 10
PROFILE_SAMPLES = 2048

# ---------------------------------------------------------------- level 1: the mortar, the launder, the chutes
MORTAR_X = (8.0, 40.0)
MORTAR_Z = (18.5, 29.5)
MORTAR_Y = (3.0, 16.5)                       # on the mortar block (oak, y 0..3): low, so the heads show over its rim
MORTAR_WALL = 1.5
MORTAR_FLOOR = 6.0                           # the floor's top; the dies stand on it
SCREEN_Y = (11.0, 15.2)                      # the screen in the front wall, over the dies
FEED_LIP_Y = 14.0                            # the feed opening in the back wall, over the lip
FEED_X = (11.0, 37.0)                        # the feed opening (x)
LAUNDER_Z = (6.0, 12.4)                      # the launder along the front: its front board's north face, its back board's south face
LAUNDER_Y = (4.4, 8.5)                       # its floor's underside, its sides' top
LAUNDER_X = (8.5, 39.5)
# The shoot: an iron spout from the feed lip up under the tray's front edge (and the feed box's floor), sides on it.
SHOOT_LO = (FEED_LIP_Y, MORTAR_Z[1])         # (y, z) its working face's lower end, the lip's back edge
SHOOT_HI = (19.5, 32.5)                      # its upper end
SHOOT_X = (18.0, 30.0)
SHOOT_T = 0.8

# The chutes are the game's own chute section (shapes/block/metal/chute/straight.json, game 1.22.7), so a player's
# chute line meets them seamlessly: the infeed, a vanilla `chute-straight-ud` placed on the infeed cell's top face,
# carries straight on down the machine's own section into the feed box or the feeder's hopper; the output, a vanilla
# `chute-straight-ns` placed against the output cell's north face, takes the launder's pulp on. Its four walls, as the
# game has them along x (the `we` chute; voxels in a block, 16 a block, UVs on the game's 16 scale), turned as the
# blocktype turns them (`ud`: rotateZ 90; `ns`: rotateY 90, about the block's centre), and cut to the length the
# machine holds: 8 across, walls 1 thick round a 6 x 6 bore, in the block's middle (4..12). Its texture is the
# chute's, game:block/metal/sheet/copper1 (the pack patches only the chute *section* item's metal, not the chute
# block's texture). validate_stampbattery checks these against the game's file when the game is installed.
VANILLA_CHUTE = (
    ("North", (0.0, 4.0, 4.0), (16.0, 12.0, 5.0),
     {"north": ((0.0, 0.0, 8.0, 16.0), 270), "east": ((13.0, 0.0, 14.0, 8.0), 0), "south": ((4.0, 0.0, 12.0, 16.0), 90),
      "west": ((0.0, 0.0, 1.0, 8.0), 0), "up": ((0.0, 8.0, 16.0, 9.0), 0), "down": ((0.0, 4.0, 16.0, 5.0), 0)}),
    ("South", (0.0, 4.0, 11.0), (16.0, 12.0, 12.0),
     {"north": ((4.0, 0.0, 12.0, 16.0), 90), "east": ((11.0, 0.0, 12.0, 8.0), 0), "south": ((4.0, 0.0, 12.0, 16.0), 270),
      "west": ((15.0, 0.0, 16.0, 8.0), 0), "up": ((0.0, 2.0, 16.0, 3.0), 0), "down": ((0.0, 4.0, 16.0, 5.0), 0)}),
    ("Bottom", (0.0, 4.0, 5.0), (16.0, 5.0, 11.0),
     {"east": ((5.0, 0.0, 11.0, 1.0), 0), "west": ((0.5, 0.0, 6.5, 1.0), 0), "up": ((5.0, 0.0, 11.0, 16.0), 90),
      "down": ((0.0, 5.0, 16.0, 11.0), 0)}),
    ("Top", (0.0, 11.0, 5.0), (16.0, 12.0, 11.0),
     {"east": ((5.0, 0.0, 11.0, 1.0), 0), "west": ((0.0, 2.0, 1.0, 8.0), 90), "up": ((0.0, 5.0, 16.0, 11.0), 0),
      "down": ((5.0, 0.0, 11.0, 16.0), 90)}),
)
CHUTE_IN_LEN = 16.0                          # the infeed section: the whole top cell, down to the hopper's mouth
CHUTE_OUT_LEN = LAUNDER_Z[0]                 # the output section: from the front face to the launder's front board

# ---------------------------------------------------------------- level 1: the feed box (plank), behind the mortar
FEEDBOX_X = (17.5, 30.5)                     # its sides' outer faces
FEEDBOX_SLOPE = math.radians(50.0)           # its floor's fall to the shoot
FEEDBOX_Z = (SHOOT_HI[1] - 0.1, 43.0)        # its floor's working face, from over the shoot's top to its back board
FEEDBOX_SIDE = 6.0                           # its sides' height over the floor

# ---------------------------------------------------------------- the water line
# Pipes and Power Expanded's straight pipe in copper or lead, drawn as UnifiedPipes draws those pipes, soldered
# (Pipes/tools/solderedpipe.py), as the rosser's drip pipes are: ppex's tube round its bore, a hub where two lengths
# meet. From the west face's inlet it runs east through the west end frame between its middle and back posts, turns
# north beside the hopper, and drops in front of the west end of the shoot, open over the feed opening's west end:
# the water goes into the mortar with the ore at either level. A part per metal (`pipecopper`, `pipelead`); a renderer
# draws the fitted metal's.
PIPE_METALS = ("copper", "lead")
PIPE_HALF = solderedpipe.HALF
WATER_Y, WATER_Z = 40.0, 40.0                # the inlet's axis: the water cell's centre, on the ppex pipe beyond the face
PIPE_X = 13.8                                # the north run and the drop (x): west of the hopper and the feed box
PIPE_Z = 29.8                                # the drop (z): behind the stamps' guides and heads, over the back wall
PIPE_END_Y = 21.5                            # the drop's open end, over the feed opening (the drop under a block long)

# ---------------------------------------------------------------- the power train (the frame's)
ENTRY = (8.0, 40.0)                          # (y, z): the entry shaft and the line shaft (the power cell's centre)
LINE = ENTRY
LINE_R = 1.4
GEARCASE_X, GEARCASE_Y, GEARCASE_Z = (42.0, 46.5), (0.0, 15.0), (34.6, 43.4)
PED_W_X, PED_M_X = (3.5, 6.5), (22.0, 26.0)  # the line shaft's pedestals (x)
BULL_X = (0.5, 3.0)                          # the drive plane: the bull pulley and its belt, outside the west end frame
BULL_R = 9.4                                 # the bull pulley on the camshaft
LINE_PULLEY_R = 4.7                          # the line shaft's pulley under it
BELT_T, BELT_IN = 0.8, 0.35                  # a belt's thickness, and how far its edges stand in from the pulley's faces

LINE_RATIO = -1.0                            # the line shaft per radian of the axle's travel (the rectifier: one way)
CAM_RATIO = LINE_RATIO * LINE_PULLEY_R / BULL_R   # the camshaft (an open belt: the same sense)
TURNS_PER_REV = abs(1.0 / CAM_RATIO)         # axle turns a camshaft turn

# ---------------------------------------------------------------- level 2: the Challenge feeder
# Rose (1898, pp. 122-123): "the tray, A, below the sheet-iron hopper, B, is revolved in a horizontal plane by means
# of a gear-wheel below it ... and this gears with teeth set in the bottom of the tray ... The gear-wheel is set in
# motion by a friction grip, D, placed on the outside of the frame, and actuated through the lever, E, by the
# bumper-rod, G, against which the tappet of the stamp strikes. At each partial rotation a given quantity of ore is
# scraped off by the stationary wings or side plates, H, resting on the tray". Every feeder of the kind is "a
# pyramidal hopper with inclined floor, connected with the feed-opening of the mortar-box by a spout or shoot" (p. 121).
HOPPER_X = (17.0, 31.0)                      # its side walls' outer faces
HOPPER_SHEET = 0.5
HOPPER_FRONT_Z = POST_M[1]                   # its front wall against the front bearer's back face
HOPPER_BACK_Z = POST_B[0]                    # its back wall against the back bearer's front face
HOPPER_FRONT_Y = 29.0                        # the front wall's bottom edge: the throat is under it
HOPPER_FLOOR_HI = (37.5, HOPPER_BACK_Z - HOPPER_SHEET)   # (y, z): the inclined floor's upper end, at the back wall
HOPPER_FLOOR_LO = (25.5, 36.5)               # its lower end, over the tray
TRAY_C = (24.0, 37.0)                        # (x, z): the spindle, behind stamp 3
TRAY_R = 6.0
TRAY_Y = (22.0, 23.0)                        # the tray's plate
TEETH_N = 16                                 # the teeth set in its bottom
TEETH_R = (3.6, TRAY_R)
TEETH_Y = (20.8, TRAY_Y[0] + 0.1)        # their top a little into the plate (its strips stand a hair apart)
TEETH_W = 0.6
MESH_R = 4.8                                 # where the pinion meshes them (the pitch radius)
PINION_N = 6
PINION_R = MESH_R * PINION_N / TEETH_N       # the pinion's pitch radius: 1.8
PINION_TIP = PINION_R + 0.5
PINION_HUB = 1.0
PINION_X = (TRAY_C[0] + MESH_R - 0.6, TRAY_C[0] + MESH_R + 0.6)
SHAFT_Y = (TEETH_Y[0] + TRAY_Y[0]) / 2 - PINION_R     # the pinion's shaft: the pitch lines meet at the teeth's middle
FEED_AXIS = (SHAFT_Y, TRAY_C[1])             # (y, z): the shaft along x, under the tray's east side
FEED_SHAFT_X = (26.4, 35.2)
FEED_SHAFT_R = 0.7
STRAP_Y = (14.0, HOPPER_FLOOR_LO[0])         # the two hanger straps from the hopper's side walls down to the step bracket
STRAP_Z = (TRAY_C[1] - 1.2, TRAY_C[1] + 1.2)
STEP_Y = (14.0, 15.5)                        # the step bracket across the straps' feet: the spindle's step bearing on it
SPINDLE_R = 0.8
DRUM_X = (31.6, 33.0)                        # the friction grip's drum, outside the east strap
DRUM_R = 2.4
LEVER_X = (33.2, 34.2)                       # the lever E: loose on the shaft beside the drum
LEVER_PIN_Z = 27.5                           # E's arm runs north to its cross pin, over the mortar's back wall
LEVER_TAIL_Z = 41.6                          # and south to its counterweight
BUMPER_X = 26.9                              # the bumper rod G (x): between stamps 3 and 4, behind their stems
BUMPER_R = 0.5
PUSH = 1.0                                   # how far stamp 3's feeding tappet pushes G down when the stamp lands
TAPPET_F_Y = 50.0                            # the feeding tappet's lug's underside, the stamp resting on its die
GUIDE_HI_TOP = RAIL_HI_Y[1] + GUIDE_H        # the upper guide girts' top
FEED_SMOOTH = 0.03                           # the bumper's drop, smoothed (radians of the camshaft): the strike's impact
GRIP_SMOOTH = 0.04                           # the grip's turn, smoothed: the friction grip takes hold and lets go
FEED_HARMONICS = 24


def stem_x(i):
    return STEM_X0 + STEM_PITCH * i


# ---------------------------------------------------------------- box helpers
def skin(el, tex):
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
REF = {"x": (1, 2), "y": (2, 0), "z": (1, 0)}


def frame_of(axis):
    a = AX[axis]
    u, w = REF[axis]
    return a, u, w


def radial(axis, c, a0, a1, r0, r1, width, ang, name, part, tex):
    """A box along an axis, from r0 to r1 out from the centre c, `width` across, turned `ang` (radians)."""
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
    """A plain round part: k strips as long as the 2k-gon is across, 180/k degrees apart, each a hair shorter than
    the last so no two ends share a plane (k 4: an octagon; 3: a hexagon)."""
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
    w = 2 * r_out * math.tan(math.pi / n)
    out = []
    for i in range(n):
        s = 0.02 if i % 2 else 0.0
        out.append(radial(axis, c, a0 + s, a1 - s, r_in, r_out, w, phase + TAU * i / n, f"{name}{i + 1}", part, tex))
    return out


def pulley(axis, c, a0, a1, r, name, part, rim_tex, hub_tex, hub_r, rim_t=1.6, n=12, arms=6):
    """A spoked wheel: a rim of n boxes, `arms` arms and an octagonal hub standing 0.2 out either side."""
    out = annulus(axis, c, a0, a1, r - rim_t, r, n, f"{name}_rim", part, rim_tex)
    out += [radial(axis, c, a0 + 0.3, a1 - 0.3, hub_r - 0.2, r - rim_t + 0.2, 1.2, TAU * (i + 0.5) / arms, f"{name}_arm{i + 1}", part, rim_tex)
            for i in range(arms)]
    out += disc(axis, c, a0 - 0.2, a1 + 0.2, hub_r, f"{name}_hub", part, hub_tex)
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


def yz(p, x=0.0):
    """A (y, z) point as a 3D point at x."""
    return (x, p[0], p[1])


def rot2(ang, v):
    """(y, z) turned by ang about +x (+y towards +z)."""
    c, s = math.cos(ang), math.sin(ang)
    return (v[0] * c - v[1] * s, v[0] * s + v[1] * c)


def plank(p0, p1, x0, x1, name, part, tex="planks", t=PLANK, down=None):
    """A board across x0..x1 whose working face runs from p0 to p1 ((y, z)); its thickness t lies on the side
    `down` (a (y, z) direction; by default the lower side)."""
    dy, dz = p1[0] - p0[0], p1[1] - p0[1]
    n = math.hypot(dy, dz)
    nrm = (-dz / n, dy / n)
    if down is None:
        down = (-1.0, 0.0)
    if nrm[0] * down[0] + nrm[1] * down[1] < 0:
        nrm = (-nrm[0], -nrm[1])
    xm = (x0 + x1) / 2
    a = (xm, p0[0] + nrm[0] * t / 2, p0[1] + nrm[1] * t / 2)
    b = (xm, p1[0] + nrm[0] * t / 2, p1[1] + nrm[1] * t / 2)
    return strut(a, b, t, x1 - x0, name, part, tex, axis="x")


# ---------------------------------------------------------------- belts
def tangents(c1, r1, c2, r2):
    """The open belt's two straight runs between circles (y, z) c1, r1 and c2, r2: for each, the angle of the
    radius to its tangent points (the same on both circles) and the points."""
    d = math.hypot(c2[0] - c1[0], c2[1] - c1[1])
    alpha = math.atan2(c2[1] - c1[1], c2[0] - c1[0])
    beta = math.acos((r1 - r2) / d)
    out = []
    for s in (1.0, -1.0):
        g = alpha + s * beta
        e = (math.cos(g), math.sin(g))
        out.append((g, (c1[0] + r1 * e[0], c1[1] + r1 * e[1]), (c2[0] + r2 * e[0], c2[1] + r2 * e[1])))
    return alpha, beta, out


def belt(c1, r1, c2, r2, x0, x1, name, part, seg_deg=36.0):
    """An open leather belt on two pulleys (rims of radius r1 at c1 and r2 at c2, (y, z)) in the plane x0..x1: its
    two straight runs, tangent to both rims, and its wraps round each rim as segments (a belt looks alike as it
    runs, so it is drawn still). Alternate pieces stand in 0.02 at their edges, so no two share a side plane."""
    rc1, rc2 = r1 + BELT_T / 2, r2 + BELT_T / 2           # the belt's centre line
    alpha, beta, runs = tangents(c1, rc1, c2, rc2)
    a0, a1 = x0 + BELT_IN, x1 - BELT_IN
    out = []
    for k, (g, p, q) in enumerate(runs):
        s = 0.02 if k % 2 else 0.0
        out.append(strut(yz(p, (a0 + a1) / 2), yz(q, (a0 + a1) / 2), BELT_T, a1 - a0 - 2 * s, f"{name}_run{k + 1}", part, "leather"))
    # the wraps: round c1 from alpha + beta the long way (through alpha + pi) to alpha - beta; round c2 from
    # alpha - beta through alpha to alpha + beta
    for tag, c, r, g0, sweep in (("a", c1, r1, alpha + beta, TAU - 2 * beta), ("b", c2, r2, alpha - beta, 2 * beta)):
        n = max(2, math.ceil(math.degrees(sweep) / seg_deg))
        dg = sweep / n
        w = 2 * (r + BELT_T) * math.tan(dg / 2) + 0.05
        for i in range(n):
            s = 0.02 if (i + 1) % 2 else 0.04
            ang = g0 + dg * (i + 0.5)
            # radial() turns +y towards +z: a box first along +y, turned by ang
            out.append(radial("x", yz(c), a0 + s, a1 - s, r, r + BELT_T, w, ang, f"{name}_wrap{tag}{i + 1}", part, "leather"))
    return out


# ---------------------------------------------------------------- the stamp's lift
def _target(x):
    """The stamp's lift (voxels) at x radians of the camshaft into its period (pi): the cam's rise at CAM_RB a radian
    from 0, let go at LIFT, thrown and falling freely onto its die over FALL, then resting."""
    x %= math.pi
    tr = LIFT / CAM_RB
    if x <= tr:
        return CAM_RB * x
    t = x - tr
    g = 2 * (LIFT + CAM_RB * FALL) / FALL ** 2
    return LIFT + CAM_RB * t - g * t * t / 2 if t <= FALL else 0.0


def harmonics_of(vals, count, smooth):
    """The mean and the first `count` harmonics (n, sin coefficient, cos coefficient) of samples over the period pi
    (even steps), each scaled by a Gaussian smoothing's transfer, exp(-(2 n smooth)^2 / 2)."""
    m = len(vals)
    a0 = sum(vals) / m
    out = []
    for n in range(1, count + 1):
        s = 2 / m * sum(v * math.sin(2 * n * math.pi * k / m) for k, v in enumerate(vals))
        c = 2 / m * sum(v * math.cos(2 * n * math.pi * k / m) for k, v in enumerate(vals))
        w = math.exp(-((2 * n * smooth) ** 2) / 2)
        out.append((n, s * w, c * w))
    return a0, out


def series(h, x):
    """A harmonic series' periodic part (no mean) at x."""
    return sum(s * math.sin(2 * n * x) + c * math.cos(2 * n * x) for n, s, c in h)


def _profile():
    """The lift's harmonics: (mean, [(n, sin coefficient, cos coefficient)]) of the period pi, smoothed; and the
    series' least value."""
    m = PROFILE_SAMPLES
    a0, out = harmonics_of([_target(math.pi * k / m) for k in range(m)], HARMONICS, SMOOTH)
    lo = min(a0 + series(out, math.pi * k / m) for k in range(m))
    return a0, out, lo


PROFILE_MEAN, PROFILE_H, PROFILE_LO = _profile()


def lift_of(x):
    """The stamp's drawn lift (voxels, 0 its least) at x radians of the camshaft into its period."""
    return PROFILE_MEAN - PROFILE_LO + series(PROFILE_H, x)


def _cam_shave():
    """How far the cams' working faces are drawn inside the true involute so the tappet, which follows the drawn
    lift, never sinks into its cam: the most the drawn lift falls short of the cam's rise, and 0.02 more."""
    tr = LIFT / CAM_RB
    worst = max(CAM_RB * (tr * k / 400) - lift_of(tr * k / 400) for k in range(401))
    return max(0.0, worst) + 0.02


CAM_SHAVE = _cam_shave()
DRAWN_LIFT = PROFILE_MEAN - PROFILE_LO        # the stamps are drawn this far over their rest: the harmonics' mean


def stamp_phase(i):
    """Stamp i's (0-based) first pick-up, in radians of the camshaft: the ten drops a turn evenly spaced in the
    firing order."""
    return ORDER.index(i + 1) * math.pi / 5


def cam_angle(psi):
    """The camshaft's working angle (radians, growing as it lifts) after the axle's travel psi."""
    return abs(CAM_RATIO) * psi


def stamp_lift(i, psi):
    return lift_of(cam_angle(psi) - stamp_phase(i))


def cam_profile(i, lobe, t):
    """A point (y, z, relative to the camshaft) of stamp i's cam, lobe 0 or 1, at the involute's roll t, as the cam
    is drawn (psi 0): the point that meets the tappet's underside, CAM_RB * t over the camshaft on the contact line
    CAM_RB south of it, when its lobe has turned that far. The camshaft turns negatively about x (its south side
    rises), so the drawn point is the contact turned back, positively."""
    phi = stamp_phase(i) + math.pi * lobe + t - CAM_T0
    return rot2(phi, (CAM_RB * t, CAM_RB))


def cam_normal(i, lobe, t):
    phi = stamp_phase(i) + math.pi * lobe + t - CAM_T0
    return rot2(phi, (1.0, 0.0))


# ---------------------------------------------------------------- the feeder's motion (stamp 3's period)
FEED_STAMP = 2                               # 0-based: stamp 3, the middle one, carries the feeding tappet
LEVER_R = TRAY_C[1] - LEVER_PIN_Z            # E's arm from the shaft to its cross pin (horizontal at rest)


def _feeder_motion():
    """Over stamp 3's period (x radians of the camshaft from its pick-up): G's drop below its stop, which the feeding
    tappet pushes while the stamp is less than PUSH over its die (fitted, smoothed and lowered so G never stands
    over its stop or into the tappet); E's turn, which carries G's foot; and the grip's one-way turn, which follows E
    down and holds while E comes back up, a steady turn plus a periodic remainder. Returns each as (mean, harmonics)
    and the figures the checks and the rig use."""
    m = PROFILE_SAMPLES
    xs = [math.pi * k / m for k in range(m)]
    want = [min(0.0, lift_of(x) - PUSH) for x in xs]            # G's drop: in contact while the stamp is low
    g0, gh = harmonics_of(want, FEED_HARMONICS, FEED_SMOOTH)
    over = max(g0 + series(gh, x) - w for x, w in zip(xs, want))
    g0 -= over + 0.005                                          # never over the stop or into the tappet
    drop = [g0 + series(gh, x) for x in xs]
    angle = [math.asin(d / LEVER_R) for d in drop]              # E's turn about +x: its arm points north (-z)
    e0, eh = harmonics_of(angle, FEED_HARMONICS, 0.0)
    # the grip turns with E only while E goes down, and stands while E comes back up: the one-way sum of E's ideal
    # turn (the drop as the tappet pushes it), whose steady part is a rotate and whose remainder is fitted, smoothed
    ideal = [math.asin(w / LEVER_R) for w in want]
    grip, acc = [], 0.0
    for k in range(m):
        grip.append(acc)
        acc += min(0.0, ideal[(k + 1) % m] - ideal[k])
    per = acc                                                   # the grip's turn over one period (negative)
    rest = [grip[k] - per * xs[k] / math.pi for k in range(m)]  # its periodic remainder
    s0, sh = harmonics_of(rest, FEED_HARMONICS, GRIP_SMOOTH)
    return {"drop": (g0, gh), "lever": (e0, eh), "grip": (s0, sh), "per": per}


FEED = _feeder_motion()
GRIP_PER_STRIKE = FEED["per"]                # radians of the grip (and pinion) a strike: one a period of stamp 3
TRAY_RATIO = -PINION_N / TEETH_N             # the tray's turn per radian of the pinion's (teeth in its bottom)


def bumper_drop(x):
    g0, gh = FEED["drop"]
    return g0 + series(gh, x)


def lever_angle(x):
    e0, eh = FEED["lever"]
    return e0 + series(eh, x)


def grip_angle(x_total):
    """The grip's turn after the camshaft has turned x_total radians into stamp 3's periods from its first pick-up
    (any real x): the steady turn plus the remainder."""
    s0, sh = FEED["grip"]
    return FEED["per"] * x_total / math.pi + s0 + series(sh, x_total)


def feed_x(psi):
    """x, the camshaft's angle into stamp 3's periods (from its first pick-up), after the axle's travel psi."""
    return cam_angle(psi) - stamp_phase(FEED_STAMP)


# The feeder's parts are drawn at their mean pose (as the stamps at their mean lift): G at its mean drop, E at its mean
# turn, the grip and pinion at their turn at psi 0 less the harmonics' value there (so the rig's rotate and swings
# add up to the turn), and the tray likewise. The design pose (G on its stop, E's arm level, a pinion tooth up
# between two of the tray's teeth) is turned from these by the rig.
G_MEAN = FEED["drop"][0]
E_MEAN = FEED["lever"][0]
GRIP_MEAN = FEED["per"] * (-stamp_phase(FEED_STAMP)) / math.pi + FEED["grip"][0]   # the grip's constant at psi 0
TRAY_MEAN = TRAY_RATIO * GRIP_MEAN


# ---------------------------------------------------------------- builders: the frame
def build_frame():
    f = "frame"
    out = []
    for tag, (x0, x1) in (("w", END_W), ("e", END_E)):
        out.append(box([x0, SILL_Y[0], SILL_Z[0]], [x1, SILL_Y[1], SILL_Z[1]], f"fr_sill_{tag}", f, "oak"))
        out.append(box([x0, CAP_Y[0], CAP_Z[0]], [x1, CAP_Y[1], CAP_Z[1]], f"fr_cap_{tag}", f, "oak"))
        for i, (pz0, pz1) in enumerate((POST_F, POST_M), 1):
            out.append(box([x0, SILL_Y[1], pz0], [x1, CAP_Y[0], pz1], f"fr_post_{tag}{i}", f, "oak"))
        out.append(box([x0 + 0.02, 0.0, POST_B[0]], [x1 - 0.02, CAP_Y[0], POST_B[1]], f"fr_post_{tag}3", f, "oak"))
        for name, (y0, y1) in (("lo", RAIL_LO_Y), ("cam", (CAM_Y - 2.0 - 3.5, CAM_Y - 2.0)), ("hi", RAIL_HI_Y)):
            out.append(box([x0 + RAIL_IN, y0, POST_F[1]], [x1 - RAIL_IN, y1, POST_M[0]], f"fr_rail{name}_{tag}", f, "oak"))
    out.append(box([END_W[1], CAP_Y[0], POST_F[0]], [END_E[0], CAP_Y[1], POST_F[1]], "fr_tie_f", f, "oak"))
    out.append(box([END_W[1], CAP_Y[0], TIE_C_Z[0]], [END_E[0], CAP_Y[1], TIE_C_Z[1]], "fr_tie_c", f, "oak"))
    out.append(box([END_W[1], CAP_Y[0], TIE_B_Z[0]], [END_E[0], CAP_Y[1], TIE_B_Z[1]], "fr_tie_b", f, "oak"))
    out.append(box([END_W[1], BEARER_Y[0], POST_M[0]], [END_E[0], BEARER_Y[1], POST_M[1]], "fr_bearer_f", f, "oak"))
    out.append(box([END_W[1], BEARER_Y[0], POST_B[0]], [END_E[0], BEARER_Y[1], POST_B[1]], "fr_bearer_b", f, "oak"))
    # the water line's hanger: an iron strap from the front bearer down onto the pipe's hub behind the mortar
    out.append(box([PIPE_X - 1.5, WATER_Y + PIPE_HALF, POST_M[0] + 0.3], [PIPE_X + 1.5, BEARER_Y[0], POST_M[0] + 2.3], "fr_hanger", f, "iron"))
    out += build_guides()
    out += build_power_frame()
    return out


def build_guides():
    """The guide girts, oak, on the rails: each two strips either side of the stems' line, the stems in the slot
    between them."""
    out = []
    hole = STEM_R                            # the stems run against both strips
    xa, xb = END_W[0] + RAIL_IN, END_E[1] - RAIL_IN
    for tag, (y0, y1) in (("lo", (RAIL_LO_Y[1], RAIL_LO_Y[1] + GUIDE_H)), ("hi", (RAIL_HI_Y[1], RAIL_HI_Y[1] + GUIDE_H))):
        out += [box([xa, y0, STEM_Z - 2.6], [xb, y1, STEM_Z - hole], f"fr_guide{tag}_f", "frame", "oak"),
                box([xa + 0.02, y0, STEM_Z + hole], [xb - 0.02, y1, STEM_Z + 2.0], f"fr_guide{tag}_b", "frame", "oak")]
    return out


def build_power_frame():
    """The power train's fixed parts: the rectifier's gear case at the power cell (its gears are inside it, not
    drawn) and the line shaft's two pedestals."""
    f = "frame"
    (gx0, gx1), (gy0, gy1), (gz0, gz1) = GEARCASE_X, GEARCASE_Y, GEARCASE_Z
    ly, lz = LINE
    out = [box([gx0 - 0.5, 0.0, gz0 + 0.3], [gx1 + 0.5, 1.5, gz1 - 0.3], "fr_gearcase_foot", f, "iron"),
           box([gx0, 1.5, gz0], [gx1, gy1, gz1], "fr_gearcase", f, "iron"),
           box([gx0 + 0.6, gy1, gz0 + 0.6], [gx1 - 0.6, gy1 + 0.8, gz1 - 0.6], "fr_gearcase_lid", f, "iron"),
           box([gx0 - 1.0, ly - 2.6, lz - 2.6], [gx0, ly + 2.6, lz + 2.6], "fr_gearcase_boss", f, "iron")]
    for tag, (x0, x1) in (("w", PED_W_X), ("m", PED_M_X)):
        out += [box([x0 - 0.6, 0.0, lz - 3.6], [x1 + 0.6, 1.2, lz + 3.6], f"fr_ped{tag}_foot", f, "iron"),
                box([x0, 1.2, lz - 2.6], [x1, ly + 2.6, lz + 2.6], f"fr_ped{tag}", f, "iron")]
    return out


def build_drive():
    """The entry shaft (the vanilla axle's cross, oak) into the gear case; the line shaft out of it, west, in its two
    pedestals."""
    out = []
    y, z = ENTRY
    out.append(box([GEARCASE_X[1] - 0.5, y - 1.0, z - 2.0], [48.0, y + 1.0, z + 2.0], "entry_shafta", "entry", "oak"))
    out.append(box([GEARCASE_X[1] - 0.5, y - 2.0, z - 1.0], [47.99, y + 2.0, z + 1.0], "entry_shaftb", "entry", "oak"))
    out += disc("x", yz(LINE), BULL_X[0] + 0.3, GEARCASE_X[0] + 0.5, LINE_R, "line_shaft", "line", "steel")
    return out


def build_line_pulleys():
    """The line shaft's pulley under the bull pulley (level 1, with the camshaft)."""
    out = disc("x", yz(LINE), *BULL_X, LINE_PULLEY_R, "linepulley", "linepulley", "iron", k=4)
    out += disc("x", yz(LINE), BULL_X[0] - 0.2, BULL_X[1] + 0.2, 2.2, "linepulley_boss", "linepulley", "iron")
    return out


# ---------------------------------------------------------------- the game's chute section
def chute_section(cell, turn, length, name, part):
    """VANILLA_CHUTE's walls cut to their last `length` along the chute (their UVs cropped alike along it, on this
    model's 64 scale), turned as the blocktype turns its variant (`turn`: an axis and degrees, about the block's
    centre), in `cell`."""
    o = [cell[k] * B for k in range(3)]
    k = TEX / 16
    out = []
    for wall, lo, hi, faces in VANILLA_CHUTE:
        a, b = B - length, B                                 # along the chute (the game's x)
        f0 = (a - lo[0]) / (hi[0] - lo[0])
        el_lo, el_hi = [a, lo[1], lo[2]], [b, hi[1], hi[2]]
        fs = {}
        for d, (uv, rotation) in faces.items():
            u0, v0, u1, v1 = uv
            if d in ("north", "south", "up", "down"):          # faces whose horizontal runs along the chute: crop it
                if rotation in (90, 270):
                    v0 = v0 + (v1 - v0) * f0
                else:
                    u0 = u0 + (u1 - u0) * f0
            face = {"texture": "#chute", "uv": [round(u0 * k, 4), round(v0 * k, 4), round(u1 * k, 4), round(v1 * k, 4)]}
            if rotation:
                face["rotation"] = rotation
            fs[d] = face
        c = [o[i] + (el_lo[i] + el_hi[i]) / 2 for i in range(3)]
        el = El(f"{name}_{wall.lower()}", [el_hi[i] - el_lo[i] for i in range(3)], c, [r[:] for r in IDENT], fs, part)
        rotate([el], turn[0], turn[1], [o[0] + 8.0, o[1] + 8.0, o[2] + 8.0])
        out.append(el)
    return out


def chute_origin(cell):
    return [cell[k] * B for k in range(3)]


# ---------------------------------------------------------------- builders: level 1
def build_mortar():
    """The mortar set: the oak mortar block, the cast-iron mortar box with five dies, the screen in its front wall and
    the feed opening in its back; the apron under the screen and the launder along the front, on two legs, out
    through the output chute's section to the front face; the shoot from the feed lip; and the infeed chute's
    section in the top face, hung from the frame's two top ties over the feed box or the hopper."""
    p = "mortar"
    (x0, x1), (z0, z1), (y0, y1) = MORTAR_X, MORTAR_Z, MORTAR_Y
    w = MORTAR_WALL
    fx0, fx1 = FEED_X
    out = [box([x0 - 0.5, 0.0, z0 - 1.0], [x1 + 0.5, y0, z1 + 0.5], "mortar_block", p, "oak"),
           box([x0, y0, z0], [x1, MORTAR_FLOOR, z1], "mortar_floor", p, "iron"),
           box([x0, MORTAR_FLOOR, z0], [x1, SCREEN_Y[0], z0 + w], "mortar_front_lo", p, "iron"),
           box([x0, SCREEN_Y[1], z0], [x1, y1, z0 + w], "mortar_front_hi", p, "iron"),
           box([x0, SCREEN_Y[0], z0], [x0 + w, SCREEN_Y[1], z0 + w], "mortar_front_w", p, "iron"),
           box([x1 - w, SCREEN_Y[0], z0], [x1, SCREEN_Y[1], z0 + w], "mortar_front_e", p, "iron"),
           box([x0 + w, SCREEN_Y[0], z0 + 0.5], [x1 - w, SCREEN_Y[1], z0 + 1.0], "mortar_screen", p, "mesh"),
           box([x0, MORTAR_FLOOR, z1 - w], [x1, FEED_LIP_Y, z1], "mortar_back_lo", p, "iron"),
           box([x0, FEED_LIP_Y, z1 - w], [fx0, y1, z1], "mortar_back_w", p, "iron"),
           box([fx1, FEED_LIP_Y, z1 - w], [x1, y1, z1], "mortar_back_e", p, "iron"),
           box([x0, MORTAR_FLOOR, z0 + w], [x0 + w, y1, z1 - w], "mortar_end_w", p, "iron"),
           box([x1 - w, MORTAR_FLOOR, z0 + w], [x1, y1, z1 - w], "mortar_end_e", p, "iron")]
    for i in range(STAMPS):
        out += disc("y", (stem_x(i), 0.0, STEM_Z), MORTAR_FLOOR, DIE_TOP, HEAD_R, f"mortar_die{i + 1}", p, "steel")
    # the apron: from under the screen's lip down onto the launder's back board
    lz0, lz1 = LAUNDER_Z
    ly0, ly1 = LAUNDER_Y
    out.append(plank((SCREEN_Y[0] - 0.3, z0), (ly1 + 0.5, lz1 - 0.6), x0 + w, x1 - w, "mortar_apron", p, "iron", t=0.8))
    out += build_launder()
    out += build_shoot()
    out += chute_section(OUTPUT_CELL, ("y", 90.0), CHUTE_OUT_LEN, "mortar_chuteout", p)
    out += chute_section(INFEED_CELL, ("z", 90.0), CHUTE_IN_LEN, "mortar_chutein", p)
    return out


def chute_bore_x():
    """The output chute's bore across x (the game's chute: 6 wide in the block's middle)."""
    return (OUTPUT_CELL[0] * B + 5.0, OUTPUT_CELL[0] * B + 11.0)


def build_launder():
    """The launder: a plank trough along the front under the apron, on two oak legs; its front board opens where the
    output chute's section meets it, and its floor stands a little over the chute's, so the pulp runs out into it."""
    p = "mortar"
    lz0, lz1 = LAUNDER_Z
    ly0, ly1 = LAUNDER_Y
    lx0, lx1 = LAUNDER_X
    cx0, cx1 = chute_bore_x()
    out = [box([lx0, ly0, lz0], [lx1, ly0 + PLANK, lz1], "mortar_launder_floor", p, "planks"),
           box([lx0, ly0 + PLANK, lz1 - PLANK], [lx1, ly1, lz1], "mortar_launder_back", p, "planks"),
           box([lx0, ly0 + PLANK, lz0], [cx0, ly1, lz0 + PLANK], "mortar_launder_frontw", p, "planks"),
           box([cx1, ly0 + PLANK, lz0], [lx1, ly1, lz0 + PLANK], "mortar_launder_fronte", p, "planks"),
           box([lx0, ly0 + PLANK, lz0 + PLANK], [lx0 + PLANK, ly1 - 0.02, lz1 - PLANK], "mortar_launder_endw", p, "planks"),
           box([lx1 - PLANK, ly0 + PLANK, lz0 + PLANK], [lx1, ly1 - 0.02, lz1 - PLANK], "mortar_launder_ende", p, "planks")]
    for tag, (x0, x1) in (("w", (lx0 + 0.5, lx0 + 2.5)), ("e", (lx1 - 2.5, lx1 - 0.5))):
        out.append(box([x0, 0.0, lz0 + 1.0], [x1, ly0, lz1 - 1.0], f"mortar_launder_leg{tag}", p, "oak"))
    return out


def shoot_y(z):
    """The shoot's working face at z."""
    (ya, za), (yb, zb) = SHOOT_LO, SHOOT_HI
    return ya + (z - za) * (yb - ya) / (zb - za)


def build_shoot():
    """The shoot: an iron spout from the feed lip up and back under the tray's front edge, which the feed box's floor
    (level 1) and the tray's wings (level 2) put the ore onto; two low sides on it."""
    p = "mortar"
    sx0, sx1 = SHOOT_X
    out = [plank(SHOOT_LO, SHOOT_HI, sx0, sx1, "mortar_shoot", p, "iron", t=SHOOT_T)]
    rise = 2.0
    for tag, (x0, x1) in (("w", (sx0 - 0.5, sx0)), ("e", (sx1, sx1 + 0.5))):
        out.append(plank((SHOOT_LO[0] + rise, SHOOT_LO[1] + 0.1), (SHOOT_HI[0] + rise, SHOOT_HI[1]), x0, x1, f"mortar_shoot{tag}", p, "iron",
                         t=rise + SHOOT_T))
    return out


def feedbox_y(z):
    """The feed box floor's working face at z: from over the shoot's top up to its back board."""
    return SHOOT_HI[0] + 0.1 + (z - FEEDBOX_Z[0]) * math.tan(FEEDBOX_SLOPE)


def build_feed_box():
    """Level 1's feed box: a plank box behind the mortar under the infeed chute, its floor falling 50 degrees onto the
    shoot, two sides and a back board hung on the back bearer; open on top, where the chute (or the player, by hand at
    the chute's mouth) drops the ore in. The feeder takes its place at level 2."""
    p = "feedbox"
    fx0, fx1 = FEEDBOX_X
    za, zb = FEEDBOX_Z
    a, b = (feedbox_y(za), za), (feedbox_y(zb), zb)
    out = [plank(a, b, fx0 + PLANK, fx1 - PLANK, "feedbox_floor", p)]
    for tag, (sx0, sx1) in (("w", (fx0, fx0 + PLANK)), ("e", (fx1 - PLANK, fx1))):
        # a plain board from under the floor's low end to FEEDBOX_SIDE over its high end
        out.append(box([sx0, a[0] - 1.6, za], [sx1, b[0] + FEEDBOX_SIDE, zb], f"feedbox_{tag}", p, "planks"))
    out.append(box([fx0, b[0] - 0.4, zb], [fx1, BEARER_Y[1] - 0.1, HOPPER_BACK_Z], "feedbox_back", p, "planks"))
    return out


def pipe_boxes():
    """The water line as UnifiedPipes' soldered pipe (solderedpipe's boxes, build-frame voxels): the inlet from the
    west face on the axis of the ppex pipe beyond it, a hub, a run north, a hub, and a drop open at its end over the
    feed opening. Every run is shorter than a block, so there are no seams and no joints but the hubs; the inlet has
    no joint at the face (the pipe beyond brings its own half joint, as a soldered ppex pipe does)."""
    h, wy = PIPE_HALF, WATER_Y
    out = solderedpipe.hub("hub1", (PIPE_X, wy, WATER_Z), ("west", "north"))
    out += solderedpipe.tube("inlet", 0, 0.0, PIPE_X - h, (WATER_Z, wy))
    out += solderedpipe.tube("north", 2, PIPE_Z + h, WATER_Z - h, (PIPE_X, wy))
    out += solderedpipe.hub("hub2", (PIPE_X, wy, PIPE_Z), ("south", "down"))
    out += solderedpipe.tube("drop", 1, PIPE_END_Y, wy - h, (PIPE_X, PIPE_Z))
    return solderedpipe.cull_pressed(out)


def build_pipes(metal_=PIPE_METALS[0]):
    """The water line as elements of the part `pipe<metal_>` (`pipe_boxes`): the body in that metal's pipe texture,
    the joints (none here) in solder, each face's UVs solderedpipe's on this model's texture scale. Built in copper;
    `pipe_copies` makes lead."""
    p = f"pipe{metal_}"
    codes = {solderedpipe.BODY: f"#{p}", solderedpipe.SOLDER: "#solder"}
    out = []
    for b in pipe_boxes():
        size = b.size()
        faces = {d: {"texture": codes[b.role], "uv": solderedpipe.uv(d, size, TEX / 16)} for d in b.faces}
        out.append(El(f"{p}_{b.name}", size, [(b.lo[k] + b.hi[k]) / 2 for k in range(3)], [row[:] for row in IDENT], faces, p))
    return out


def pipe_copies(els):
    """The water line in the other metals: the first metal's elements as they are (after the z-fighting fix, so every
    copy gets the same insets), renamed and in their own metal's texture. Only one metal's is ever drawn, so the
    copies are not checked against each other."""
    first = f"pipe{PIPE_METALS[0]}"
    out = []
    for m in PIPE_METALS[1:]:
        for el in els:
            if el.part == first:
                copy_ = el.clone(f"pipe{m}" + el.name[len(first):], f"pipe{m}")
                for face in copy_.faces.values():
                    if face["texture"] == f"#{first}":
                        face["texture"] = f"#pipe{m}"
                out.append(copy_)
    return out


def build_camshaft():
    """The camshaft set: the camshaft in two pillow blocks on the end frames' cam rails, ten involute cams (a double
    cam beside each stem), the bull pulley at its west end outside the west end frame, belted from the line shaft's
    pulley under it."""
    c = yz(CAM_C)
    out = disc("x", c, BULL_X[0] + 0.1, END_E[1] - 0.3, SHAFT_R, "camshaft_shaft", "camshaft", "steel")
    for tag, (x0, x1) in (("w", END_W), ("e", END_E)):
        out.append(box([x0 + RAIL_IN, CAM_Y - 2.0, CAM_Z - 2.4], [x1 - RAIL_IN, CAM_Y + 2.4, CAM_Z + 2.4], f"camframe_pillow_{tag}", "camframe", "iron"))
    for i in range(STAMPS):
        xa, xb = stem_x(i) + CAM_X[0], stem_x(i) + CAM_X[1]
        xm = (xa + xb) / 2
        out += disc("x", c, xa - 0.15, xb + 0.15, CAM_HUB_R, f"camshaft_hub{i + 1}", "camshaft", "iron")
        for lobe in (0, 1):
            # the arm: the involute's working face as chords (vertices on the curve, CAM_SHAVE inside it, equal in
            # arc length), each box CAM_ARM deep behind its chord; a web from the hub to the arm's root
            s0, s1 = CAM_RB * CAM_ROOT ** 2 / 2, CAM_RB * CAM_T1 ** 2 / 2
            ts = [math.sqrt(2 * (s0 + (s1 - s0) * k / CAM_SEGS) / CAM_RB) for k in range(CAM_SEGS + 1)]
            pts = [cam_profile(i, lobe, t) for t in ts]
            for k in range(CAM_SEGS):
                (ay, az), (by, bz) = pts[k], pts[k + 1]
                ny, nz = cam_normal(i, lobe, (ts[k] + ts[k + 1]) / 2)
                off = CAM_ARM / 2 + CAM_SHAVE
                a3 = (xm, CAM_Y + ay - ny * off, CAM_Z + az - nz * off)
                b3 = (xm, CAM_Y + by - ny * off, CAM_Z + bz - nz * off)
                out.append(strut(a3, b3, CAM_ARM, xb - xa - 0.02 * (k % 2), f"camshaft_cam{i + 1}{'ab'[lobe]}{k + 1}", "camshaft", "iron"))
            ny, nz = cam_normal(i, lobe, ts[0])
            root = (pts[0][0] - ny * (CAM_ARM / 2 + CAM_SHAVE), pts[0][1] - nz * (CAM_ARM / 2 + CAM_SHAVE))
            rr = math.hypot(*root)
            inner = (root[0] * (CAM_HUB_R - 0.6) / rr, root[1] * (CAM_HUB_R - 0.6) / rr)
            out.append(strut((xm, CAM_Y + inner[0], CAM_Z + inner[1]), (xm, CAM_Y + root[0], CAM_Z + root[1]), 1.4, xb - xa - 0.04,
                             f"camshaft_cam{i + 1}{'ab'[lobe]}0", "camshaft", "iron"))
    out += pulley("x", c, *BULL_X, BULL_R, "camshaft_bull", "camshaft", "oak", "iron", 2.6, rim_t=1.8, n=14, arms=6)
    out += belt(LINE, LINE_PULLEY_R, CAM_C, BULL_R, *BULL_X, "camframe_belt", "camframe")
    return out


def build_stamps():
    """Five stamps, each a steel shoe, an iron head, a steel stem and an iron tappet, drawn at their mean lift
    (DRAWN_LIFT over resting on their dies): the harmonics move them from there."""
    out = []
    for i in range(STAMPS):
        p = f"stamp{i + 1}"
        x = stem_x(i)
        c = (x, 0.0, STEM_Z)
        y0 = DIE_TOP + DRAWN_LIFT
        out += disc("y", c, y0, y0 + SHOE_T, SHOE_R, f"{p}_shoe", p, "steel")
        out += disc("y", c, y0 + SHOE_T, y0 + SHOE_T + HEAD_T, HEAD_R, f"{p}_head", p, "iron")
        out += disc("y", c, y0 + SHOE_T + HEAD_T - 0.3, STEM_TOP + DRAWN_LIFT, STEM_R, f"{p}_stem", p, "steel", k=3)
        ty = TAPPET_Y0 + DRAWN_LIFT
        out.append(box([x - TAPPET_H, ty, STEM_Z - TAPPET_H], [x + TAPPET_H, ty + TAPPET_T, STEM_Z + TAPPET_H], f"{p}_tappet", p, "iron"))
    return out


# ---------------------------------------------------------------- builders: level 2, the Challenge feeder
def bumper_dir():
    """The unit (x, z) from stamp 3's stem to the bumper rod G."""
    dx, dz = BUMPER_X - stem_x(FEED_STAMP), LEVER_PIN_Z - STEM_Z
    n = math.hypot(dx, dz)
    return dx / n, dz / n


def build_feeder_frame():
    """The feeder's fixed parts (`feeder`): the sheet-iron hopper with its inclined floor, hung between the two bearers;
    the two hanger straps from its side walls down to the step bracket under the tray, the spindle's step bearing on
    it, the pinion shaft's two bearings (a pedestal on the bracket, a boss on the east strap); the two wings resting
    on the tray's front; and G's guide bracket on the upper guide girts' back strip."""
    p = "feeder"
    hx0, hx1 = HOPPER_X
    t = HOPPER_SHEET
    zf, zb = HOPPER_FRONT_Z, HOPPER_BACK_Z
    (fy, fz), (ly, lz) = HOPPER_FLOOR_HI, HOPPER_FLOOR_LO
    out = [box([hx0, HOPPER_FRONT_Y, zf], [hx1, BEARER_Y[1], zf + t], "feeder_hopper_front", p, "sheet"),
           box([hx0, fy, zb - t], [hx1, BEARER_Y[1], zb], "feeder_hopper_back", p, "sheet")]
    for tag, (x0, x1) in (("w", (hx0, hx0 + t)), ("e", (hx1 - t, hx1))):
        # the side walls whole, down to the throat: over the floor they close the hopper, under it they are its cheeks
        out.append(box([x0, ly, zf], [x1, BEARER_Y[1], zb], f"feeder_hopper_{tag}", p, "sheet"))
    out.append(plank((fy, fz), (ly, lz), hx0 + t, hx1 - t, "feeder_hopper_floor", p, "sheet", t=t))
    # the hanger straps, the step bracket and the spindle's step bearing
    sz0, sz1 = STRAP_Z
    for tag, (x0, x1) in (("w", (hx0, hx0 + t)), ("e", (hx1 - t, hx1))):
        out.append(box([x0, STRAP_Y[0], sz0], [x1, STRAP_Y[1], sz1], f"feeder_strap_{tag}", p, "iron"))
    out.append(box([hx0, STEP_Y[0], sz0], [hx1, STEP_Y[1], sz1], "feeder_bracket", p, "iron"))
    tx, tz = TRAY_C
    out.append(box([tx - 1.2, STEP_Y[1], tz - 1.2], [tx + 1.2, STEP_Y[1] + 1.5, tz + 1.2], "feeder_stepbearing", p, "iron"))
    # the pinion shaft's bearings: a pedestal on the bracket inside the teeth, a boss on the east strap
    sy, szc = FEED_AXIS
    out.append(box([FEED_SHAFT_X[0], STEP_Y[1], szc - 1.0], [FEED_SHAFT_X[0] + 1.0, sy + 1.0, szc + 1.0], "feeder_pedestal", p, "iron"))
    out.append(box([hx1 - 0.7, sy - 1.5, szc - 1.0], [hx1 + 0.4, sy + 1.5, szc + 1.0], "feeder_boss", p, "iron"))
    # the wings (Rose's H): two plates standing on the tray's front, from the throat's front corners out over its edge
    wy0, wy1 = TRAY_Y[1], HOPPER_FRONT_Y
    for tag, sgn in (("w", -1.0), ("e", 1.0)):
        a = (tx + sgn * 3.0, 0.0, zf + 0.3)
        b = (tx + sgn * 5.8, 0.0, tz - TRAY_R - 0.4)
        el = strut((a[0], (wy0 + wy1) / 2, a[2]), (b[0], (wy0 + wy1) / 2, b[2]), t, wy1 - wy0, f"feeder_wing_{tag}", p, "iron", axis="y")
        out.append(el)
    # G's guide bracket, bolted to the upper guide girts' back strip
    gz0 = STEM_Z + 2.0
    out.append(box([BUMPER_X - 1.0, RAIL_HI_Y[1] + 0.5, gz0], [BUMPER_X + 1.0, GUIDE_HI_TOP - 0.5, LEVER_PIN_Z + 1.1], "feeder_guide", p, "iron"))
    return out


def build_tray():
    """The tray (Rose's A) on its spindle, drawn at its mean turn: a cast-iron plate, its hub and nut, and the teeth set
    in its bottom, which the pinion meshes; the spindle stands in the step bearing."""
    p = "tray"
    tx, tz = TRAY_C
    c = (tx, 0.0, tz)
    out = disc("y", c, TRAY_Y[0], TRAY_Y[1], TRAY_R, "tray_plate", p, "iron", k=6)
    out += disc("y", c, TEETH_Y[0] + 0.3, TRAY_Y[0], 1.6, "tray_hub", p, "iron", k=3)
    out += disc("y", c, TRAY_Y[1], TRAY_Y[1] + 0.6, 1.1, "tray_nut", p, "iron", k=3)
    out += disc("y", c, STEP_Y[1] + 0.3, TRAY_Y[1] + 0.3, SPINDLE_R, "tray_spindle", p, "steel", k=3)
    for k in range(TEETH_N):
        # a gap where the pinion meshes (+x) at the design pose: the teeth half a pitch either side of it
        ang = TAU * (k + 0.5) / TEETH_N
        el = box([tx + TEETH_R[0], TEETH_Y[0], tz - TEETH_W / 2], [tx + TEETH_R[1], TEETH_Y[1], tz + TEETH_W / 2], f"tray_tooth{k + 1}", p, "iron")
        rotate([el], "y", math.degrees(ang), [tx, 0.0, tz])
        out.append(el)
    rotate(out, "y", math.degrees(TRAY_MEAN), [tx, 0.0, tz])
    return out


def build_feedshaft():
    """The pinion's shaft (Rose's gear-wheel), drawn at its mean turn: the shaft in its two bearings, the pinion meshing
    the tray's teeth, the friction grip's drum outside the east strap, a collar at its end."""
    p = "feedshaft"
    c = yz(FEED_AXIS)
    out = disc("x", c, FEED_SHAFT_X[0] + 0.05, FEED_SHAFT_X[1], FEED_SHAFT_R, "feedshaft_shaft", p, "steel", k=3)
    out += disc("x", c, PINION_X[0], PINION_X[1], PINION_HUB, "feedshaft_pinion", p, "iron", k=3)
    for k in range(PINION_N):
        # a tooth up into the tray's gap at the design pose
        out.append(radial("x", c, PINION_X[0] + 0.02 * (k % 2), PINION_X[1] - 0.02 * (k % 2), PINION_HUB - 0.2, PINION_TIP, TEETH_W,
                          TAU * k / PINION_N, f"feedshaft_tooth{k + 1}", p, "iron"))
    out += disc("x", c, DRUM_X[0], DRUM_X[1], DRUM_R, "feedshaft_drum", p, "iron", k=6)
    out += disc("x", c, FEED_SHAFT_X[1] - 0.8, FEED_SHAFT_X[1] + 0.02, 1.1, "feedshaft_collar", p, "iron", k=3)
    rotate(out, "x", math.degrees(GRIP_MEAN), c)
    return out


def build_lever():
    """The lever E, drawn at its mean turn: loose on the shaft beside the drum, its arm north to the cross pin G hangs on,
    its tail south to the counterweight that holds G up on its stop, and the grip's shoe (Rose's D) bearing on the
    drum's rim: it takes the drum round with it as G pushes the arm down, and slips back over it as the weight lifts
    it."""
    p = "lever"
    sy, sz = FEED_AXIS
    x0, x1 = LEVER_X
    out = disc("x", yz(FEED_AXIS), x0, x1, 1.3, "lever_hub", p, "iron", k=3)
    out.append(box([x0 + 0.01, sy - 0.6, LEVER_PIN_Z], [x1 - 0.01, sy + 0.6, sz - 0.8], "lever_arm", p, "iron"))
    out.append(box([x0 + 0.01, sy - 0.6, sz + 0.8], [x1 - 0.01, sy + 0.6, LEVER_TAIL_Z], "lever_tail", p, "iron"))
    out.append(box([x0 - 0.4, sy - 2.2, LEVER_TAIL_Z - 1.0], [x1 + 0.4, sy + 2.2, LEVER_TAIL_Z + 1.2], "lever_weight", p, "iron"))
    out += disc("x", yz((sy, LEVER_PIN_Z)), BUMPER_X - 0.7, x1 - 0.02, 0.5, "lever_pin", p, "steel", k=3)
    top = sy + DRUM_R * 1.04
    out.append(box([DRUM_X[0] + 0.2, top, sz - 1.4], [x1 - 0.02, top + 0.7, sz + 1.4], "lever_shoe", p, "iron"))
    out.append(box([x0 + 0.02, sy + 1.2, sz - 0.6], [x1 - 0.04, top, sz + 0.6], "lever_web", p, "iron"))
    rotate(out, "x", math.degrees(E_MEAN), yz(FEED_AXIS))
    return out


def bumper_top():
    """G's head's top on its stop (the design pose): PUSH over the feeding tappet's lug, the stamp resting."""
    return TAPPET_F_Y + PUSH


def build_bumper():
    """The bumper rod G, drawn at its mean drop: an eye on E's cross pin, the rod up through its guide bracket, a stop
    collar under the bracket, and its head under the feeding tappet's lug."""
    p = "bumper"
    sy = FEED_AXIS[0]
    c = (BUMPER_X, 0.0, LEVER_PIN_Z)
    top = bumper_top()
    out = [box([BUMPER_X - 0.7, sy - 0.9, LEVER_PIN_Z - 0.7], [BUMPER_X + 0.7, sy + 0.9, LEVER_PIN_Z + 0.7], "bumper_eye", p, "iron")]
    out += disc("y", c, sy + 0.9, top - 1.0, BUMPER_R, "bumper_rod", p, "steel", k=3)
    out += disc("y", c, RAIL_HI_Y[1] + 0.5 - 1.0, RAIL_HI_Y[1] + 0.5, 0.9, "bumper_stop", p, "iron", k=3)
    out += disc("y", c, top - 1.0, top, 1.0, "bumper_head", p, "iron", k=3)
    translate(out, [0.0, G_MEAN, 0.0])
    return out


def build_feed_tappet():
    """The feeding tappet on stamp 3 (Rose's F), drawn with the stamp at its mean lift: a collar near the top of the
    stem, above the upper guide girts, and a lug reaching back to over G's head."""
    p = "feedtappet"
    x = stem_x(FEED_STAMP)
    y0 = TAPPET_F_Y + DRAWN_LIFT
    dx, dz = bumper_dir()
    reach = math.hypot(BUMPER_X - x, LEVER_PIN_Z - STEM_Z) + 0.9
    out = [box([x - 1.4, y0, STEM_Z - 1.4], [x + 1.4, y0 + 1.6, STEM_Z + 1.4], "feedtappet_collar", p, "iron")]
    a = (x + dx * 1.2, y0 + 0.5, STEM_Z + dz * 1.2)
    b = (x + dx * reach, y0 + 0.5, STEM_Z + dz * reach)
    out.append(strut(a, b, 1.6, 1.0, "feedtappet_lug", p, "iron", axis="y"))
    return out


def build_feeder():
    return build_feeder_frame() + build_tray() + build_feedshaft() + build_lever() + build_bumper() + build_feed_tappet()


def build():
    els = build_frame() + build_drive() + build_line_pulleys()
    els += build_mortar() + build_camshaft() + build_stamps() + build_feed_box()
    els += build_feeder() + build_pipes()
    return els


# ---------------------------------------------------------------- the levels
# A level is only parts fitted to the same equipment (#711). Level 2's feeder goes where level 1's feed box was, so
# fitting it takes the feed box off: the levels' sets are the scenario's states, not nested.
LEVELS = {"level1": ("mortar", "camshaft", "stamps", "feedbox"), "level2": ("mortar", "camshaft", "stamps", "feeder")}
LEVEL_VALUES = ("mortar", "camshaft", "stamps", "feedbox", "feeder")
PIPE_REQUIRES = tuple(f"pipe{m}" for m in PIPE_METALS)
STATES = ("frame", "level1", "level2")


def in_state(requires, state):
    """Whether a part with this `requires` is drawn in a build state ("frame", "level1", "level2"): at each level
    the water line in its first metal (the others are copies, never drawn with it)."""
    if requires == PIPE_REQUIRES[0]:
        return state != "frame"
    return requires is None or (state != "frame" and requires in LEVELS[state])


# ---------------------------------------------------------------- rig
def r6(v):
    return round(v + 0.0, 6) + 0.0


def pt(*v):
    return [r6(x / B) for x in v]


def norm_phase(p):
    return math.remainder(p, TAU)


def period_drivers(h, i, motion, axis, scale, pivot=None):
    """A function of stamp i's period (harmonics h of x, its camshaft angle from its pick-up) as drivers on the axle's
    travel: one slide or swing a harmonic, the n-th of the period pi, so 2n times the camshaft's turn, shifted to the
    stamp's phase. `scale` turns the series' unit (voxels or radians) into the driver's."""
    k = abs(CAM_RATIO)
    out = []
    for n, s, c in h:
        amp = math.hypot(s, c)
        ph = math.atan2(c, s) - 2 * n * stamp_phase(i)
        d = {"type": motion, "axis": axis}
        if pivot is not None:
            d["pivot"] = pivot
        d.update({"amplitude": r6(amp * scale), "ratio": r6(2 * n * k), "phase": r6(norm_phase(ph)), "input": "travel"})
        out.append(d)
    return out


def stamp_drivers(i):
    """Stamp i's lift as harmonics of the axle's travel."""
    return period_drivers(PROFILE_H, i, "slide", "y", 1.0 / B)


def staircase_drivers(axis, pivot, ratio_scale):
    """The grip's one-way turn (times ratio_scale: 1 for the shaft, TRAY_RATIO for the tray) as a steady rotate on the
    axle's travel plus its periodic remainder's harmonics as swings about the same axis."""
    per = FEED["per"] * ratio_scale
    rot_ = {"type": "rotate", "axis": axis, "pivot": pivot, "ratio": r6(per / math.pi * abs(CAM_RATIO)), "input": "travel"}
    sh = [(n, s * ratio_scale, c * ratio_scale) for n, s, c in FEED["grip"][1]]
    return [rot_] + period_drivers(sh, FEED_STAMP, "swing", axis, 1.0, pivot)


_PARTS = []


def rig_parts():
    if not _PARTS:
        _PARTS.extend(_rig_parts())
    return copy.deepcopy(_PARTS)


def _rig_parts():
    def rot_x(centre, ratio, inp=None):
        return dict({"type": "rotate", "axis": "x", "pivot": pt(*yz(centre)), "ratio": r6(ratio)}, **({"input": inp} if inp else {}))
    feed_pivot = pt(*yz(FEED_AXIS))
    tray_pivot = pt(TRAY_C[0], 0.0, TRAY_C[1])
    parts = [
        {"id": "entry", "match": ["entry_*"], "requires": None, "drivers": [rot_x(ENTRY, 1.0)]},
        {"id": "line", "match": ["line_*"], "requires": None, "drivers": [rot_x(LINE, LINE_RATIO, "travel")]},
        # level 1: the battery
        {"id": "linepulley", "match": ["linepulley*"], "requires": "camshaft", "drivers": [rot_x(LINE, LINE_RATIO, "travel")]},
        {"id": "camshaft", "match": ["camshaft_*"], "requires": "camshaft", "drivers": [rot_x(CAM_C, CAM_RATIO, "travel")]},
        {"id": "camframe", "match": ["camframe_*"], "requires": "camshaft", "drivers": []},
    ]
    for i in range(STAMPS):
        parts.append({"id": f"stamp{i + 1}", "match": [f"stamp{i + 1}_*"], "requires": "stamps", "drivers": stamp_drivers(i)})
    parts += [
        {"id": "mortar", "match": ["mortar_*"], "requires": "mortar", "drivers": []},
        {"id": "feedbox", "match": ["feedbox_*"], "requires": "feedbox", "drivers": []},
        # level 2: the Challenge feeder, worked by stamp 3's drop
        {"id": "feedtappet", "match": ["feedtappet_*"], "requires": "feeder", "ride": f"stamp{FEED_STAMP + 1}", "drivers": []},
        {"id": "bumper", "match": ["bumper_*"], "requires": "feeder", "drivers": period_drivers(FEED["drop"][1], FEED_STAMP, "slide", "y", 1.0 / B)},
        {"id": "lever", "match": ["lever_*"], "requires": "feeder",
         "drivers": period_drivers(FEED["lever"][1], FEED_STAMP, "swing", "x", 1.0, feed_pivot)},
        {"id": "feedshaft", "match": ["feedshaft_*"], "requires": "feeder", "drivers": staircase_drivers("x", feed_pivot, 1.0)},
        {"id": "tray", "match": ["tray_*"], "requires": "feeder", "drivers": staircase_drivers("y", tray_pivot, TRAY_RATIO)},
        {"id": "feeder", "match": ["feeder_*"], "requires": "feeder", "drivers": []},
        # the water line, one part per metal (requires pipecopper, pipelead): only the fitted metal's is drawn
        *({"id": r, "match": [f"{r}_*"], "requires": r, "drivers": []} for r in PIPE_REQUIRES),
        {"id": "frame", "match": ["fr_*"], "requires": None, "drivers": []},
    ]
    for p in parts:
        p.setdefault("ride", None)
        for d in p["drivers"]:
            validate_driver(d)
    return parts


# ---------------------------------------------------------------- poses
REST = (0.0, 0.0)                            # (theta, travel): the authored pose's inputs


def inputs_of(pose):
    th, ps = pose[:2]
    return {"theta": th, "travel": ps}


def pm(parts, pid, pose):
    return _part_matrix(parts, pid, inputs_of(pose))


def turning(psi, theta=None):
    """A pose of the axle having travelled psi (its angle psi, unless given)."""
    return (psi if theta is None else theta, psi)


def at_cam(phi):
    """A pose with the camshaft turned phi radians the working way."""
    psi = phi / abs(CAM_RATIO)
    return (psi, psi)


def at_feed(x):
    """A pose with the camshaft x radians into stamp 3's periods (from its first pick-up)."""
    return at_cam(x + stamp_phase(FEED_STAMP))


# ---------------------------------------------------------------- the rig file
def footprint():
    return [(x, y, z) for x in range(CELLS_X) for y in range(CELLS_Y) for z in range(CELLS_Z)]


ANCHORS = ("water", "infeed", "output")
CELL_ANCHORS = (("power", POWER_CELL, POWER_FACE), ("water", WATER_CELL, WATER_FACE), ("infeed", INFEED_CELL, INFEED_FACE),
                ("output", OUTPUT_CELL, OUTPUT_FACE))


def anchor_points():
    """The anchors' points (build frame, voxels): the water line's inlet, the infeed chute's mouth (the infeed cell's
    top face's centre, on a vanilla chute's axis), the output chute's mouth (the output cell's north face's centre)."""
    return {"water": (0.0, WATER_Y, WATER_Z),
            "infeed": (INFEED_CELL[0] * B + 8.0, CELLS_Y * B, INFEED_CELL[2] * B + 8.0),
            "output": (OUTPUT_CELL[0] * B + 8.0, OUTPUT_CELL[1] * B + 8.0, 0.0)}


def make_rig(parts):
    rig = {"_comment": f"Generated by {SCRIPT}. Native frame, block units, controller cell at [0,0,0]: the bottom middle cell of the "
                       "north face, the output chute's. One machine, its levels parts fitted to it (#711): the frame's parts have no "
                       "requires; level 1 fits mortar, camshaft, stamps and feedbox, level 2 the feeder in the feed box's place "
                       "(levels); the water line is pipecopper or pipelead, the same pipe in the fitted metal. Hand feeding goes "
                       "in at the infeed's mouth, as a chute does, at both levels. theta turns the entry shaft; everything "
                       "behind the rectifier's gear case follows travel (psi): the line shaft, the camshaft, the stamps "
                       "(harmonics of the camshaft's turn) and the feeder (stamp 3's strikes). No work. See the stamp "
                       "battery's README for the schema.",
           "cells": []}
    for name, cell, face in CELL_ANCHORS:
        rig[f"{name}Cell"] = list(cell)
        rig[f"{name}Face"] = face
    for key, p in anchor_points().items():
        rig[key] = {"pos": pt(*p)}
    rig["stamps"] = {"turnsPerRev": r6(TURNS_PER_REV), "dropsPerRev": 2, "firingOrder": list(ORDER), "lift": r6(LIFT / B),
                     "_comment": f"turnsPerRev: axle turns per camshaft turn as the belt is drawn (the line pulley's radius "
                                 f"{LINE_PULLEY_R} to the bull pulley's {BULL_R}, behind a 1:1 rectifier); each stamp drops "
                                 "dropsPerRev times a camshaft turn, in firingOrder, and is lifted `lift` blocks by its cam."}
    rig["feeder"] = {"stamp": FEED_STAMP + 1, "strikesPerRev": 2, "push": r6(PUSH / B),
                     "trayDegreesPerStrike": r6(math.degrees(abs(GRIP_PER_STRIKE * TRAY_RATIO))),
                     "_comment": "Level 2, the Challenge feeder: the feeding tappet on `stamp` strikes the bumper rod each time "
                                 "the stamp lands (strikesPerRev a camshaft turn), pushing it `push` blocks down; the grip turns "
                                 "the pinion and the tray a step each strike."}
    rig["levels"] = {"1": list(LEVELS["level1"]), "2": list(LEVELS["level2"]),
                     "_comment": "The requires values fitted at each level. The feeder goes where the feed box was: fitting it "
                                 "takes the feed box off. From level 1 the water line is fitted too, in one metal: pipecopper or "
                                 "pipelead."}
    rig["parts"] = parts
    return rig


# ---------------------------------------------------------------- shipped
def shipped(els, parts, rig):
    """The build-frame model and rig moved so ORIGIN_CELL is [0,0,0]."""
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
    for name, _, _ in CELL_ANCHORS:
        ship[f"{name}Cell"] = shift_cell(rig[f"{name}Cell"], ORIGIN_CELL)
    for key in ANCHORS:
        ship[key] = {"pos": shift_point(rig[key]["pos"], db)}
    ship["parts"] = ship_parts
    return ship_els, ship_parts, ship


def complete(requires):
    """The finished machine the cells' boxes are built from: the frame, level 2 and the water line in copper."""
    return requires is None or requires in LEVELS["level2"] or requires == PIPE_REQUIRES[0]


def shipped_cells(shape, ship_parts):
    """The cells' boxes from the shipped shape as written, posed at rest by the shipped rig: the finished machine's
    (`complete`); a cell with none is hollow. Then a lid on every column: the top is one deck, where the player stands
    to feed by hand at the infeed's mouth."""
    written = flatten(shape["elements"], textures={})
    requires = {p["id"]: p["requires"] for p in ship_parts}
    by_cell = {}
    for w in written:
        pid = part_of(ship_parts, w.name)
        if not complete(requires[pid]):
            continue
        el = posed(w, _part_matrix(ship_parts, pid, inputs_of(REST)))
        lo, hi = el.aabb()
        for c in cells_touched(lo, hi):
            by_cell.setdefault(c, []).append(el)
    out = []
    for c in footprint():
        pos = tuple(c[k] - ORIGIN_CELL[k] for k in range(3))
        cell = {"pos": list(pos)}
        bs = cell_boxes(by_cell[pos], pos) if pos in by_cell else None
        if bs:
            cell["boxes"] = bs
        else:
            cell["hollow"] = True
        out.append(cell)
    return with_lids(out)


def check_shipped(els, parts, ship_els, ship_parts, ship):
    d = [-ORIGIN_CELL[k] * B for k in range(3)]
    poses = [REST, turning(0.3), turning(4.77, -2.0), at_cam(1.1), at_feed(2.2), turning(31.0, 1.0), turning(101.0, -7.0)]
    worst = worst_shift_error(els, ship_els, lambda el, pose: pm(parts, el.part, pose),
                              lambda el, pose: _part_matrix(ship_parts, el.part, inputs_of(pose)), d, poses)
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
    """theta and psi: the axle at rest, either way, through several turns, at every stamp's pick-up, mid-lift,
    release and landing, stamp 3's strike on the bumper through it, and a long run."""
    out = []
    for th in (0.0, 1.1, -2.3, 2.9, -5.4):
        out.append((th, abs(th)))
    for extra in (0.37, 7.3, 13.9, 25.1, 50.27, 101.0, 333.3):
        out.append((1.3, round(1.3 + extra, 6)))
    for i in range(STAMPS):
        for x in (0.0, 0.4, LIFT / CAM_RB, LIFT / CAM_RB + 0.45, LIFT / CAM_RB + FALL, 2.6):
            psi = r6((stamp_phase(i) + x) / abs(CAM_RATIO))
            out.append((r6(-psi), psi))
    for x in (1.85, 1.95, 2.0, 2.05, 2.1, 2.5, 3.1, 3.3, 3.5):
        psi = r6(at_feed(x)[1])
        out.append((psi, psi))
    return out


def reference_json(ship_parts):
    poses = []
    for pose in reference_poses():
        th, ps = pose
        mats = {q["id"]: round_matrix(_part_matrix(ship_parts, q["id"], inputs_of(pose))) for q in ship_parts}
        poses.append({"theta": th, "travel": ps, "matrices": mats})
    return {"_comment": f"Generated by {SCRIPT} from the shipped stampbattery-rig.json's parts: each part's matrix as 3 rows of 4 "
                        "(block units) at each pose of the axle's angle (theta) and travel (psi). The site's and the Python "
                        "tests check their rig maths against it.",
            "poses": poses}


# ---------------------------------------------------------------- shape files
def shape_json(els):
    return machine_shape_json(
        els, f"Generated by {SCRIPT}. Every element was made for the Seraph Horizons mod. Every level's parts are in this "
             "shape; the rig's requires say which level each belongs to. Keep element names when editing: the rig finds "
             "its parts by them.", TEXTURES, tex_size=TEX)


def coplanar_poses():
    """(pose, state): the machine at each level at rest and running, so faces that never show together are never
    compared."""
    return (REST + ("level2",), at_cam(0.7) + ("level1",), at_cam(2.3) + ("level2",), at_feed(2.3) + ("level2",),
            at_feed(0.6) + ("level1",), REST + ("frame",))


REQUIRES = {}


def shown(posed_els, pose):
    """The posed elements of the pose's build state; the rest moved far away (copies; the order kept), so the
    z-fighting fix and its check deal only with faces that show together."""
    state = pose[2] if len(pose) > 2 else "level2"
    out = []
    for e in posed_els:
        if not in_state(REQUIRES[e.part], state):
            e = e.clone()
            e.c = [e.c[0], e.c[1] - 1000.0 - 10.0 * len(out), e.c[2]]
        out.append(e)
    return out


def fix_coplanar(els, parts):
    return fix_coplanar_posed(els, lambda es, pose: shown([posed(el, pm(parts, el.part, pose)) for el in es], pose), coplanar_poses())


def main():
    ap = argparse.ArgumentParser(description="Generate the stamp battery's shapes, rig and reference poses.")
    ap.add_argument("--out", type=Path, help="write the four files into this directory instead of the mod's assets and tests")
    ap.add_argument("--quick", action="store_true", help="skip the z-fighting fix and the slow checks (not for files that ship)")
    args = ap.parse_args()
    import validate_stampbattery
    els = build()
    for el in els:
        el.r = [[0.0 if abs(v) < 1e-9 else (math.copysign(1.0, v) if abs(abs(v) - 1) < 1e-9 else v) for v in row] for row in el.r]
    parts = rig_parts()
    REQUIRES.update({p["id"]: p["requires"] for p in parts})
    if not args.quick:
        before, hidden = fix_coplanar(els, parts)
        for pose, pairs in before.items():
            print(f"coplanar faces before the fix at {pose}: {len(pairs)} pairs")
        print(f"coplanar faces: {hidden} faces pressed against their own part removed")
    els += pipe_copies(els)
    rig = make_rig(parts)
    ok = validate_stampbattery.validate(sys.modules[__name__], els, parts, rig, quick=args.quick)
    if args.out:
        outs = (args.out / "stampbattery.json", args.out / "stampbattery_frame.json", args.out / "stampbattery-rig.json",
                args.out / "rig-reference.json")
    else:
        outs = (SHAPE_DIR / "stampbattery.json", SHAPE_DIR / "stampbattery_frame.json", RIG_DIR / "stampbattery-rig.json", REFERENCE_OUT)
    ship_els, ship_parts, ship = shipped(els, parts, rig)
    shape, frame_shape = shape_json(ship_els), shape_json([el for el in ship_els if el.part == "frame"])
    ship["cells"] = shipped_cells(shape, ship_parts)
    ok = validate_stampbattery.validate_files(sys.modules[__name__], shape, frame_shape, ship) and ok
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
