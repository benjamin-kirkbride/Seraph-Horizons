#!/usr/bin/env python3
"""Generate the handcar's shapes, rig, rider animations and reference poses.

The handcar is a standard-gauge rail car for Yang's Transport Tycoon (`yangtransport`), pumped by
hand: a walking beam on an A-frame stand in the middle of the deck rocks on a pivot pin, a pitman
(connecting rod) from its front arm turns a crank on a countershaft under the deck, and the
countershaft's wooden gear (24 teeth) drives an 8-tooth pinion on the front axle, so the wheels turn
three times per stroke of the beam. Two riders stand at the beam's ends, facing each other, both
hands on its handles. A cargo deck behind the rear rider takes a chest or a crate. Everything is
built here from plain boxes; no other mod's model is used.

It writes, deterministically,

    handcar.json          the car body, every moving part as a joint with the "pump"
                          animation that turns them   (assets/seraphhorizons/shapes/entity/)
    handcar-axlebox.json  Yang's bogie shape for this car: the four journal boxes, which ride the
                          track at the axles           (assets/seraphhorizons/shapes/entity/)
    handcar-rig.json      the part rig, the anchors, the cycle and the bogie offsets
                                                       (assets/seraphhorizons/config/)
    handcar-riders.json   a JSON patch adding the riders' two animations to the seraph and their
                          metadata to the player         (assets/seraphhorizons/patches/)
    rig-reference.json    every part's matrix at a grid of axle angles   (tests/Handcar/)
    rider-reference.json  the hands' targets at sampled frames, and the seraph's numbers they were
                          solved against                 (tests/Handcar/)

or, with `--out DIR`, all of them into DIR. It validates its own output (validate_handcar.py) and exits
non-zero if a check fails. The seraph's shape is read from the game named by VINTAGE_STORY.

Frames. Everything is in voxels in the car's model frame as Yang's standard-gauge renderer draws a
body: x along the car with the front (Yang's EndA, where the car goes when it goes forward) towards
-x, y up from the rails' top, z across with the track's centre line at z 16. The model point
(8, 0, 16) is the body's pose point. A rider's own frame is the seraph's: it faces -x, its feet at
(8, 0, 8).

The rig's one input is theta, the axle's angle (radians): the wheels turn by theta, the countershaft
by -theta/3, and the beam and pitman by their linkage. The car rolls forward (-x) by theta times the
wheel's radius, so one cycle of the pump, 6 pi of theta, is 6 pi r of travel.
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

from machinegen.geometry import IDENT, El, rotate  # noqa: E402
from machinegen.output import element_json, reference_dumps, rig_dumps, round_matrix  # noqa: E402
from machinegen.rigmath import part_matrix, part_of, posed, validate_driver  # noqa: E402

import riders  # noqa: E402

ROOT = Path(__file__).resolve().parents[4]
MOD = ROOT / "mods-src" / "seraphhorizons"
SHAPE_DIR = MOD / "assets" / "seraphhorizons" / "shapes" / "entity"
RIG_DIR = MOD / "assets" / "seraphhorizons" / "config"
PATCH_DIR = MOD / "assets" / "seraphhorizons" / "patches"
TEST_DIR = MOD / "tests" / "Handcar"
SCRIPT = "mods-src/seraphhorizons/Handcar/tools/make_shape.py"

B = 16.0
TAU = 2 * math.pi

TEXTURES = {
    "oak": "game:block/wood/debarked/oak",
    "planks": "game:block/wood/planks/oak1",
    "copper": "game:block/metal/plate/copper",
}
TEX = 64                                     # shape texture units: 4 per voxel, one texture across a block face

# ---------------------------------------------------------------- the pump (it decides where everything else goes)
PIVOT = (16.0, 29.0)                         # the beam's pivot pin, along z, on the stand: 17 over the deck
ARM = 10.0                                   # pivot to each handle
PIN_ARM = 5.0                                # pivot to the pitman's pin, on the front arm (half the handle's arm)
BEAM_HALF_H = 1.5
BEAM_Z = (14.5, 17.5)
BEAM_PIN = (PIVOT[0] - PIN_ARM, PIVOT[1])    # the pitman's upper pin, over the countershaft
BEAM_PIN_Z = (17.5, 21.2)
PITMAN_Z = (19.6, 20.8)
PITMAN_W = 1.2
EYE = 1.8                                    # the pitman's eyes (square, copper)
HANDLE_Z = (9.5, 22.5)                       # the crossbars at the beam's ends
HANDLE_HALF = 0.7
CHEEKS_Z = ((12.6, 14.4), (17.6, 19.4))      # the stand's two A-frames, either side of the beam
PIVOT_PIN_Z = (12.2, 19.8)
PIVOT_PIN_R = 0.6
STAND_FEET_X = (9.5, 22.5)
STAND_TOP = ((PIVOT[0] - 1.6, PIVOT[0] + 1.6), (PIVOT[1] - 1.8, PIVOT[1] + 1.8))   # the bearing blocks at the cheeks' tops
STAND_LEG_W = 1.8
STAND_TIE_Y = (19.4, 20.6)                   # a cross tie between each cheek's legs
CRANK_R = 1.7                                # the crank's throw: the beam swings about 20 degrees each way

# ---------------------------------------------------------------- the track, the wheels
MID_Z = 16.0                                 # the track's centre line
RAIL_Z = (3.5, 28.5)                         # the rails' centres: Yang's standard gauge (25 voxels)
R_WHEEL = 5.0                                # tread radius: the wheels stand on the rails' top, y 0
R_FLANGE = 5.6
AXLE_Y = R_WHEEL
TREAD_W = 1.6
FLANGE_W = 0.5
HUB_R = 1.2
AXLE_R = 0.8                                 # the axle's octagon (apothem)
AXLE_Z = (0.8, 31.2)                         # its ends run in the journal boxes
JOURNAL_Z = ((0.4, 2.4), (29.6, 31.6))       # the journal boxes (the bogie shape), outside the wheels
JOURNAL_HALF = (1.6, 1.6)                    # half their size along x and y

# ---------------------------------------------------------------- the gearing (module 0.4)
MODULE = 0.4
PINION_TEETH, GEAR_TEETH = 8, 24
RATIO = GEAR_TEETH / PINION_TEETH            # 3 axle turns per stroke
R_PINION = PINION_TEETH * MODULE / 2         # 1.6
R_GEAR = GEAR_TEETH * MODULE / 2             # 4.8
GEAR_Z = (15.2, 16.8)
COUNTER = (BEAM_PIN[0], R_WHEEL)             # the countershaft, under the beam's pin, level with the axles
COUNTER_R = 0.7
COUNTER_Z = (12.6, 18.6)
COUNTER_BEARINGS_Z = ((13.0, 14.4), (17.2, 18.4))
CRANK_Z = (18.6, 19.4)
CRANK_PIN_Z = (18.6, 20.9)
CRANK_PIN_R = 0.5
X_FRONT = COUNTER[0] - R_PINION - R_GEAR     # the front axle carries the pinion
X_REAR = 42.0                                # under the cargo deck
WHEELBASE = X_REAR - X_FRONT

# ---------------------------------------------------------------- the frame and the deck
DECK_X = (-8.0, 58.0)
DECK_Y = (11.0, 12.0)                        # boards; the riders stand on y 12
DECK_Z = (0.4, 31.6)
SILL_Y = (JOURNAL_HALF[1] + AXLE_Y, DECK_Y[0])   # the sills sit on the journal boxes
SILL_Z = JOURNAL_Z
BOARDS = (0.4, 8.3, 16.2, 24.1, 31.6)        # the deck's four boards across
SLOT_X = (COUNTER[0] - CRANK_R - 1.4, COUNTER[0] + CRANK_R + 1.2)   # the pitman's slot through the third board

# The crank's angle alpha = -theta / RATIO (the countershaft turns the other way to the axle); at
# theta 0 the crank pin points forward along +x (alpha 0) and the beam is level.
HARMONICS = 4                                # Fourier terms for the beam's and the pitman's angles

# ---------------------------------------------------------------- the riders and the cargo
REACH = 6.0                                  # a rider's feet stand this far behind the handle
SEATS = {"front": {"pos": (PIVOT[0] - ARM - REACH, DECK_Y[1], MID_Z), "turn": 180.0, "handle": PIVOT[0] - ARM},  # faces +x
         "rear": {"pos": (PIVOT[0] + ARM + REACH, DECK_Y[1], MID_Z), "turn": 0.0, "handle": PIVOT[0] + ARM}}     # faces -x
SEAT_BOX_HALF = (5.0, 18.0, 6.0)             # the standing places' selection boxes: x half, height, z half
GRIP_Z = 3.3                                 # the hands either side of the beam's centre line
CARGO = ((41.0, DECK_Y[1], 8.0), (57.0, DECK_Y[1] + 16.0, 24.0))    # a chest's block on the cargo deck

# ---------------------------------------------------------------- the branch selector (Yang's turn lever)
LEVER_PIVOT = (16.0, DECK_Y[1] + 0.6, 26.0)
LEVER_LEN = 7.0
LEVER_TILT = 25.0                            # left is +z (the car's left, facing its front)

# ---------------------------------------------------------------- the riders' animations
RIDER_FRAMES = 60                            # frames per stroke, the car's "pump" too
BODY_ANIM = "pump"
# The tiny speed keeps the game from running the animations on their own (the mod sets their frame);
# the ease speeds are scaled to match, so the game still eases them by itself at about 1 a second (the
# mod sets their easing every frame, ahead of that step).
ANIM_SPEED = 0.0001
ANIM_EASE = 10000.0
RIDER_WEIGHT = 1000.0


def grip_code(seat):
    """The seat's animation: standing, both hands on the handle (the pump's linkage is not
    symmetric about the pivot, so each seat has its own)."""
    return f"seraphhorizons-handcar-grip-{seat}"


def pump_code(seat):
    """The effort eased in over the grip while the seat's rider pumps."""
    return f"seraphhorizons-handcar-pump-{seat}"


# ---------------------------------------------------------------- box helpers (voxels)
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
    el = El(name, [hi[k] - lo[k] for k in range(3)], c, [r[:] for r in IDENT], {}, part)
    return skin(el, tex) if tex else el


def bar(a, b, w, z0, z1, name, part, tex):
    """A bar in the x-y plane from point a to point b (x, y), `w` across, z0..z1 deep."""
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy)
    mid = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (z0 + z1) / 2)
    el = box([mid[0] - length / 2, mid[1] - w / 2, z0], [mid[0] + length / 2, mid[1] + w / 2, z1], name, part, tex)
    rotate([el], "z", math.degrees(math.atan2(dy, dx)), mid)
    return el


def disc_z(c, z0, z1, r, name, part, tex, k=4, phase=0.0):
    """A round part about z: k strips 180/k degrees apart, each a hair shorter than the last."""
    half = r * math.tan(math.pi / (2 * k))
    st = min(0.012, (z1 - z0) / (4 * k))
    out = []
    for i in range(k):
        el = box([c[0] - r, c[1] - half, z0 + st * i], [c[0] + r, c[1] + half, z1 - st * i], f"{name}_{i + 1}" if k > 1 else name, part, tex)
        ang = phase + math.pi * i / k
        if abs(ang) > 1e-12:
            rotate([el], "z", math.degrees(ang), (c[0], c[1], 0.0))
        out.append(el)
    return out


def radial_z(c, z0, z1, r0, r1, width, ang, name, part, tex):
    """A box from radius r0 to r1 about the z axis through c, at angle `ang` from +x."""
    el = box([c[0] + r0, c[1] - width / 2, z0], [c[0] + r1, c[1] + width / 2, z1], name, part, tex)
    if abs(ang) > 1e-12:
        rotate([el], "z", math.degrees(ang), (c[0], c[1], 0.0))
    return el


def ring_z(c, z0, z1, r_in, r_out, n, name, part, tex, phase=0.0):
    w = 2 * r_out * math.tan(math.pi / n)
    return [radial_z(c, z0 + (0.02 if i % 2 else 0.0), z1 - (0.02 if i % 2 else 0.0), r_in, r_out, w, phase + TAU * i / n, f"{name}{i + 1}", part, tex)
            for i in range(n)]


def spur_z(c, z0, z1, pitch_r, teeth, name, part, tex, phase):
    """A spur gear about z: an octagonal body to the root circle and `teeth` teeth of MODULE."""
    root, tip = pitch_r - 1.25 * MODULE, pitch_r + MODULE
    out = disc_z(c, z0, z1, root + 0.05, f"{name}_body", part, tex)
    w = math.pi * MODULE / 2
    for i in range(teeth):
        out.append(radial_z(c, z0 + 0.03, z1 - 0.03, root - 0.2, tip, w, phase + TAU * i / teeth, f"{name}_tooth{i + 1}", part, tex))
    return out


# ---------------------------------------------------------------- the linkage
def crank_pin(alpha):
    return (COUNTER[0] + CRANK_R * math.cos(alpha), COUNTER[1] + CRANK_R * math.sin(alpha))


def beam_pin(beta):
    """The pitman's upper pin when the beam is turned by beta (about +z; the front arm points to -x)."""
    return (PIVOT[0] - (PIVOT[0] - BEAM_PIN[0]) * math.cos(beta), PIVOT[1] - (PIVOT[0] - BEAM_PIN[0]) * math.sin(beta))


PITMAN_LEN = math.dist(crank_pin(0.0), beam_pin(0.0))


def solve_beta(alpha, guess=0.0):
    """The beam's angle that keeps the pitman its length (Newton on the pin distance)."""
    b = guess
    for _ in range(50):
        p, q = crank_pin(alpha), beam_pin(b)
        f = math.dist(p, q) - PITMAN_LEN
        h = 1e-7
        q2 = beam_pin(b + h)
        df = (math.dist(p, q2) - math.dist(p, q)) / h
        step = f / df
        b -= step
        if abs(step) < 1e-13:
            break
    return b


def rod_angle(alpha, beta):
    p, q = crank_pin(alpha), beam_pin(beta)
    return math.atan2(q[1] - p[1], q[0] - p[0])


def fourier(fn, n_terms, samples=720):
    """Fit fn(alpha) (periodic) with a mean and n_terms harmonics: [(k, amplitude, phase)] with
    fn ~ sum amplitude * sin(k alpha + phase) (k 0 the mean: amplitude * sin(phase), phase pi/2)."""
    vals = [fn(TAU * i / samples) for i in range(samples)]
    out = []
    mean = sum(vals) / samples
    out.append((0, mean, math.pi / 2))
    for k in range(1, n_terms + 1):
        a = 2 / samples * sum(v * math.sin(k * TAU * i / samples) for i, v in enumerate(vals))
        b = 2 / samples * sum(v * math.cos(k * TAU * i / samples) for i, v in enumerate(vals))
        out.append((k, math.hypot(a, b), math.atan2(b, a)))
    return out


BETA_SERIES = fourier(lambda a: solve_beta(a), HARMONICS)
GAMMA0 = rod_angle(0.0, 0.0)
GAMMA_SERIES = fourier(lambda a: rod_angle(a, solve_beta(a)) - GAMMA0, HARMONICS)


def series(terms, alpha):
    return sum(amp * math.sin(k * alpha + ph) for k, amp, ph in terms)


# ---------------------------------------------------------------- builders
def build_axle(x, prefix, with_pinion):
    part = "axle_front" if prefix == "af" else "axle_rear"
    c = (x, AXLE_Y)
    els = disc_z(c, AXLE_Z[0], AXLE_Z[1], AXLE_R, f"{prefix}_axle", part, "copper")
    for side, (tz0, tz1, fz0, fz1, hz0, hz1) in (("l", (RAIL_Z[0] - TREAD_W / 2, RAIL_Z[0] + TREAD_W / 2,
                                                       RAIL_Z[0] + TREAD_W / 2, RAIL_Z[0] + TREAD_W / 2 + FLANGE_W,
                                                       JOURNAL_Z[0][1] + 0.1, RAIL_Z[0] + TREAD_W / 2 + FLANGE_W + 0.2)),
                                                 ("r", (RAIL_Z[1] - TREAD_W / 2, RAIL_Z[1] + TREAD_W / 2,
                                                       RAIL_Z[1] - TREAD_W / 2 - FLANGE_W, RAIL_Z[1] - TREAD_W / 2,
                                                       RAIL_Z[1] - TREAD_W / 2 - FLANGE_W - 0.2, JOURNAL_Z[1][0] - 0.1))):
        w = f"{prefix}_wheel{side}"
        els += disc_z(c, tz0 + 0.05, tz1 - 0.05, R_WHEEL - 0.6, f"{w}_web", part, "oak")
        els += ring_z(c, tz0, tz1, R_WHEEL - 0.65, R_WHEEL, 16, f"{w}_tyre", part, "copper")
        els += ring_z(c, fz0, fz1, R_WHEEL - 0.4, R_FLANGE, 16, f"{w}_flange", part, "copper")
        els += disc_z(c, hz0, hz1, HUB_R, f"{w}_hub", part, "copper")
        # four bolts on the outer face, so the turn shows
        outer = tz0 - 0.25 if side == "l" else tz1
        for i in range(4):
            ang = TAU * i / 4 + math.pi / 4
            px, py = c[0] + 2.7 * math.cos(ang), c[1] + 2.7 * math.sin(ang)
            els.append(box([px - 0.35, py - 0.35, outer], [px + 0.35, py + 0.35, outer + 0.25], f"{w}_bolt{i + 1}", part, "copper"))
    if with_pinion:
        els += spur_z(c, GEAR_Z[0], GEAR_Z[1], R_PINION, PINION_TEETH, f"{prefix}_pinion", part, "oak", phase=0.0)
    return els


def build_gear():
    part = "gear"
    c = COUNTER
    els = disc_z(c, COUNTER_Z[0], COUNTER_Z[1], COUNTER_R, "gr_shaft", part, "copper")
    # a gap faces the pinion (towards -x) at theta 0
    els += spur_z(c, GEAR_Z[0], GEAR_Z[1], R_GEAR, GEAR_TEETH, "gr_gear", part, "oak", phase=math.pi + math.pi / GEAR_TEETH)
    els.append(bar(c, crank_pin(0.0), 1.2, CRANK_Z[0], CRANK_Z[1], "gr_crank", part, "copper"))
    els += disc_z(crank_pin(0.0), CRANK_PIN_Z[0], CRANK_PIN_Z[1], CRANK_PIN_R, "gr_crankpin", part, "copper")
    els += disc_z(c, CRANK_Z[0], CRANK_Z[1] - 0.05, 1.0, "gr_crankboss", part, "copper")
    return els


def build_pitman():
    part = "pitman"
    p, q = crank_pin(0.0), beam_pin(0.0)
    els = [bar(p, q, PITMAN_W, PITMAN_Z[0] + 0.15, PITMAN_Z[1] - 0.15, "pm_rod", part, "oak")]
    for name, at in (("pm_eye_low", p), ("pm_eye_high", q)):
        el = box([at[0] - EYE / 2, at[1] - EYE / 2, PITMAN_Z[0]], [at[0] + EYE / 2, at[1] + EYE / 2, PITMAN_Z[1]], name, part, "copper")
        rotate([el], "z", math.degrees(rod_angle(0.0, 0.0)), (at[0], at[1], 0.0))
        els.append(el)
    return els


def build_beam():
    part = "beam"
    x0, x1 = PIVOT[0] - ARM, PIVOT[0] + ARM
    y0, y1 = PIVOT[1] - BEAM_HALF_H, PIVOT[1] + BEAM_HALF_H
    els = [box([x0, y0, BEAM_Z[0]], [PIVOT[0], y1, BEAM_Z[1]], "bm_arm_front", part, "oak"),
           box([PIVOT[0], y0, BEAM_Z[0]], [x1, y1, BEAM_Z[1]], "bm_arm_rear", part, "oak")]
    # the pivot's boss (copper straps round the beam), the pitman's lug and pin
    els.append(box([PIVOT[0] - 1.6, y0 - 0.25, BEAM_Z[0] - 0.05], [PIVOT[0] + 1.6, y1 + 0.25, BEAM_Z[1] + 0.05], "bm_boss", part, "copper"))
    els.append(box([BEAM_PIN[0] - 1.0, y0, BEAM_Z[1]], [BEAM_PIN[0] + 1.0, y1, PITMAN_Z[0] - 0.1], "bm_lug", part, "oak"))
    els += disc_z(BEAM_PIN, BEAM_PIN_Z[0], BEAM_PIN_Z[1], 0.5, "bm_pin", part, "copper")
    # the handles: a crossbar at each end (two bars, the second turned 45 degrees: an octagon), capped in copper
    for end, x in (("front", x0), ("rear", x1)):
        lo = [x - HANDLE_HALF, PIVOT[1] - HANDLE_HALF, HANDLE_Z[0]]
        hi = [x + HANDLE_HALF, PIVOT[1] + HANDLE_HALF, HANDLE_Z[1]]
        els.append(box(lo, hi, f"bm_handle{end}_bar", part, "oak"))
        bar2 = box([lo[0], lo[1], lo[2] + 0.02], [hi[0], hi[1], hi[2] - 0.02], f"bm_handle{end}_bar2", part, "oak")
        rotate([bar2], "z", 45.0, (x, PIVOT[1], 0.0))
        els.append(bar2)
        for zc, tag in ((HANDLE_Z[0], "capl"), (HANDLE_Z[1], "capr")):
            z0, z1 = (zc - 0.02, zc + 0.6) if tag == "capl" else (zc - 0.6, zc + 0.02)
            els.append(box([x - HANDLE_HALF - 0.15, PIVOT[1] - HANDLE_HALF - 0.15, z0],
                           [x + HANDLE_HALF + 0.15, PIVOT[1] + HANDLE_HALF + 0.15, z1], f"bm_handle{end}_{tag}", part, "copper"))
    return els


def build_frame():
    part = "frame"
    els = []
    # sills on the journal boxes, the length of the deck
    for i, (z0, z1) in enumerate(SILL_Z):
        n = f"fr_sill{'lr'[i]}"
        for k, (a, b) in enumerate(((DECK_X[0], 25.0), (25.0, DECK_X[1]))):
            els.append(box([a, SILL_Y[0], z0], [b, SILL_Y[1], z1], f"{n}_{k + 1}", part, "oak"))
    # cross members under the deck, at the ends and between the axles
    for k, x in enumerate((DECK_X[0] + 1.0, 22.0, DECK_X[1] - 1.0)):
        els.append(box([x - 1.0, SILL_Y[1] - 2.0, SILL_Z[0][1]], [x + 1.0, SILL_Y[1], SILL_Z[1][0]], f"fr_cross{k + 1}", part, "oak"))
    # the deck's boards, along the car; the third board is cut round the pitman
    slot_x, slot_z = SLOT_X, (PITMAN_Z[0] - 0.4, PITMAN_Z[1] + 0.4)
    for i in range(4):
        z0, z1 = BOARDS[i], BOARDS[i + 1]
        spans = [(DECK_X[0], DECK_X[1])]
        if z0 < slot_z[0] < z1:
            spans = [(DECK_X[0], slot_x[0]), (slot_x[1], DECK_X[1])]
            els.append(box([slot_x[0], DECK_Y[0], z0], [slot_x[1], DECK_Y[1], slot_z[0]], f"fr_board{i + 1}_slotl", part, "planks"))
            els.append(box([slot_x[0], DECK_Y[0], slot_z[1]], [slot_x[1], DECK_Y[1], z1], f"fr_board{i + 1}_slotr", part, "planks"))
        for k, (a, b) in enumerate(spans):
            n_seg = max(1, math.ceil((b - a) / 16.0 - 1e-9))
            for s in range(n_seg):
                sa, sb = a + (b - a) * s / n_seg, a + (b - a) * (s + 1) / n_seg
                els.append(box([sa, DECK_Y[0], z0], [sb, DECK_Y[1], z1], f"fr_board{i + 1}_{k + 1}_{s + 1}", part, "planks"))
    # the countershaft's bearings, hanging from the deck
    for k, (z0, z1) in enumerate(COUNTER_BEARINGS_Z):
        els.append(box([COUNTER[0] - 1.2, COUNTER[1] - 1.2, z0], [COUNTER[0] + 1.2, DECK_Y[0], z1], f"fr_hanger{k + 1}", part, "oak"))
    # the stand: two A-frames either side of the beam, a bearing block at each top
    (tx0, tx1), (ty0, ty1) = STAND_TOP
    for i, (z0, z1) in enumerate(CHEEKS_Z):
        n = f"fr_cheek{'lr'[i]}"
        els.append(box([tx0, ty0, z0], [tx1, ty1, z1], f"{n}_top", part, "oak"))
        els.append(bar((STAND_FEET_X[0], DECK_Y[1] - 0.6), (tx0 + 0.6, ty0 + 0.8), STAND_LEG_W, z0 + 0.02, z1 - 0.02, f"{n}_legf", part, "oak"))
        els.append(bar((STAND_FEET_X[1], DECK_Y[1] - 0.6), (tx1 - 0.6, ty0 + 0.8), STAND_LEG_W, z0 + 0.04, z1 - 0.04, f"{n}_legr", part, "oak"))
        els.append(box([STAND_FEET_X[0] - 1.4, DECK_Y[1], z0 - 0.3], [STAND_FEET_X[1] + 1.4, DECK_Y[1] + 0.8, z1 + 0.3], f"{n}_sole", part, "oak"))
        els.append(box([11.8, STAND_TIE_Y[0], z0 + 0.06], [20.2, STAND_TIE_Y[1], z1 - 0.06], f"{n}_tie", part, "oak"))
    # the pivot pin, through both tops
    els += disc_z(PIVOT, PIVOT_PIN_Z[0], PIVOT_PIN_Z[1], PIVOT_PIN_R, "fr_pivotpin", part, "copper")
    # the branch selector's quadrant on the deck
    lx, ly, lz = LEVER_PIVOT
    els.append(box([lx - 1.2, DECK_Y[1], lz - 3.0], [lx + 1.2, DECK_Y[1] + 0.8, lz + 3.0], "fr_quadrant", part, "oak"))
    els.append(box([lx - 0.4, DECK_Y[1] + 0.8, lz - 2.6], [lx + 0.4, DECK_Y[1] + 1.4, lz + 2.6], "fr_quadrant_rail", part, "copper"))
    # the cargo deck's rails, so a chest sits in a frame
    (cx0, cy0, cz0), (cx1, _, cz1) = CARGO
    for k, (a, b) in enumerate(((cz0 - 0.8, cz0), (cz1, cz1 + 0.8))):
        els.append(box([cx0, cy0, a], [cx1, cy0 + 1.0, b], f"fr_cargorail{k + 1}", part, "oak"))
    els.append(box([cx1, cy0, cz0 - 0.8], [cx1 + 0.8, cy0 + 1.0, cz1 + 0.8], "fr_cargostop", part, "oak"))
    return els


def build_levers():
    out = []
    lx, ly, lz = LEVER_PIVOT
    for name, tilt in (("TNL_LFT", LEVER_TILT), ("TNL_STR", 0.0), ("TNL_RGT", -LEVER_TILT)):
        part = {"TNL_LFT": "lever_left", "TNL_STR": "lever_straight", "TNL_RGT": "lever_right"}[name]
        handle = box([lx - 0.35, ly, lz - 0.35], [lx + 0.35, ly + LEVER_LEN, lz + 0.35], name, part, "oak")
        knob = box([lx - 0.6, ly + LEVER_LEN - 0.4, lz - 0.6], [lx + 0.6, ly + LEVER_LEN + 0.8, lz + 0.6], f"{name}_knob", part, "copper")
        rotate([handle, knob], "x", tilt, (0.0, ly, lz))
        out += [handle, knob]
    return out


def helpers():
    """Faceless boxes: the selection boxes Yang's body behaviour reads from attachment points, the
    cargo slot's mount, and nothing that is drawn."""
    els = []
    for key, seat in SEATS.items():
        sx, sy, sz = seat["pos"]
        hx, hy, hz = SEAT_BOX_HALF
        els.append(box([sx - hx, sy, sz - hz], [sx + hx, sy + hy, sz + hz], f"sel_seat{key}", "frame", None))
    els.append(box(list(CARGO[0]), list(CARGO[1]), "sel_cargo", "frame", None))
    els.append(box([DECK_X[0], SILL_Y[0], DECK_Z[0]], [DECK_X[1], DECK_Y[1], DECK_Z[1]], "sel_body", "frame", None))
    els.append(box([STAND_FEET_X[0] - 1.4, DECK_Y[1], CHEEKS_Z[0][0]], [STAND_FEET_X[1] + 1.4, STAND_TOP[1][1], CHEEKS_Z[1][1]], "sel_stand", "frame", None))
    return els


# Attachment points, by the helper element they sit on: the seats' (where the rider's feet go), the
# cargo slot's, and the forwarding hit boxes'. Yang puts a seat at the attachment point plus half its
# element's size (SGLocomotiveBodyTransform.GetCenteredAttachmentPointPosition), so a seat's point is
# written relative to its box's centre.
def attachment_points():
    aps = {}
    for key, seat in SEATS.items():
        hx, hy, hz = SEAT_BOX_HALF
        aps[f"sel_seat{key}"] = [{"code": f"SEAT_{key.upper()}_AP", "pos": [0.0, -hy / 2, 0.0]}]
    aps["sel_cargo"] = [{"code": "CARGO_AP", "pos": [0.0, 0.0, 0.0]}]
    aps["sel_body"] = [{"code": "FWHB_0", "pos": [0.0, 0.0, 0.0]}]
    aps["sel_stand"] = [{"code": "FWHB_1", "pos": [0.0, 0.0, 0.0]}]
    return aps


def build_all():
    els = (build_axle(X_FRONT, "af", True) + build_axle(X_REAR, "ar", False) + build_gear() + build_pitman()
           + build_beam() + build_frame() + build_levers() + helpers())
    return els


def axlebox_elements():
    """The bogie shape: the two journal boxes of one axle, in Yang's bogie frame (the bogie's point
    at (8, 0, 16), its axle there)."""
    els = []
    hx, hy = JOURNAL_HALF
    for i, (z0, z1) in enumerate(JOURNAL_Z):
        els.append(box([8.0 - hx, AXLE_Y - hy, z0], [8.0 + hx, AXLE_Y + hy, z1], f"journal{'lr'[i]}", "frame", "copper"))
        els.append(box([8.0 - hx - 0.3, AXLE_Y - hy + 0.4, z0 + 0.3], [8.0 - hx, AXLE_Y + hy - 0.4, z1 - 0.3], f"journal{'lr'[i]}_lidf", "frame", "copper"))
        els.append(box([8.0 + hx, AXLE_Y - hy + 0.4, z0 + 0.3], [8.0 + hx + 0.3, AXLE_Y + hy - 0.4, z1 - 0.3], f"journal{'lr'[i]}_lidr", "frame", "copper"))
    return els


# ---------------------------------------------------------------- the rig
def blocks(p):
    return [round(v / B, 6) for v in p]


def rig_parts():
    piv = lambda x, y: [round(x / B, 6), round(y / B, 6), round(MID_Z / B, 6)]  # noqa: E731
    beam = [{"type": "swing", "axis": "z", "pivot": piv(*PIVOT), "amplitude": round(amp, 8), "ratio": round(-k / RATIO, 8),
             "phase": round(ph, 8)} for k, amp, ph in BETA_SERIES if abs(amp) > 1e-9]
    p0 = crank_pin(0.0)
    rod = [{"type": "swing", "axis": "z", "pivot": piv(*p0), "amplitude": round(amp, 8), "ratio": round(-k / RATIO, 8),
            "phase": round(ph, 8)} for k, amp, ph in GAMMA_SERIES if abs(amp) > 1e-9]
    # the lower pin follows the crank: x = c cos(alpha) - c, y = c sin(alpha), alpha = -theta / 3
    rod += [{"type": "slide", "axis": "x", "amplitude": round(CRANK_R / B, 8), "ratio": round(-1 / RATIO, 8), "phase": round(math.pi / 2, 8)},
            {"type": "slide", "axis": "x", "amplitude": round(CRANK_R / B, 8), "ratio": 0.0, "phase": round(-math.pi / 2, 8)},
            {"type": "slide", "axis": "y", "amplitude": round(CRANK_R / B, 8), "ratio": round(-1 / RATIO, 8), "phase": 0.0}]
    return [
        {"id": "pitman", "match": ["pm_*"], "requires": None, "drivers": rod},
        {"id": "beam", "match": ["bm_*"], "requires": None, "drivers": beam},
        {"id": "gear", "match": ["gr_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "z", "pivot": piv(*COUNTER), "ratio": round(-1 / RATIO, 8)}]},
        {"id": "axle_front", "match": ["af_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "z", "pivot": piv(X_FRONT, AXLE_Y), "ratio": 1.0}]},
        {"id": "axle_rear", "match": ["ar_*"], "requires": None,
         "drivers": [{"type": "rotate", "axis": "z", "pivot": piv(X_REAR, AXLE_Y), "ratio": 1.0}]},
        {"id": "lever_left", "match": ["TNL_LFT*"], "requires": "left", "drivers": []},
        {"id": "lever_straight", "match": ["TNL_STR*"], "requires": "straight", "drivers": []},
        {"id": "lever_right", "match": ["TNL_RGT*"], "requires": "right", "drivers": []},
        {"id": "frame", "match": ["*"], "requires": None, "drivers": []},
    ]


def grip_points(seat_key):
    """A seat's two grips on its handle at rest (car frame): its rider's right hand and left hand.
    The rear rider faces -x, so its right is -z; the front rider faces +x, its right +z."""
    seat = SEATS[seat_key]
    x = seat["handle"]
    right = MID_Z - GRIP_Z if seat["turn"] == 0.0 else MID_Z + GRIP_Z
    left = 2 * MID_Z - right
    return {"R": (x, PIVOT[1], right), "L": (x, PIVOT[1], left)}


def cycle_info():
    r = R_WHEEL / B
    return {"animation": BODY_ANIM, "frames": RIDER_FRAMES, "axleTurns": RATIO, "wheelRadius": round(r, 6),
            "distancePerCycle": round(TAU * RATIO * r, 6)}


def rig_json(parts):
    rig = {"_comment": f"Generated by {SCRIPT}; do not edit. The handcar's part rig (one input: theta, the axle's angle), its "
                       "anchors and cycle, in blocks, in the car's model frame (front towards -x, the rails' top at y 0, the "
                       "track's centre line at z 1). See mods-src/seraphhorizons/Handcar/README.md."}
    for key, seat in SEATS.items():
        rig[f"seat{key.capitalize()}"] = {"pos": blocks(seat["pos"]), "turn": seat["turn"]}
    for key in SEATS:
        for side, p in grip_points(key).items():
            rig[f"grip{key.capitalize()}{side}"] = {"pos": blocks(p), "part": "beam"}
    rig["cargo"] = {"pos": blocks(((CARGO[0][0] + CARGO[1][0]) / 2, CARGO[0][1], (CARGO[0][2] + CARGO[1][2]) / 2))}
    rig["cycle"] = cycle_info()
    rig["riders"] = {key: {"grip": grip_code(key), "pump": pump_code(key)} for key in SEATS}
    rig["bogies"] = {"bodyOffsetForward": round((8.0 - X_FRONT) / -B, 6), "front": 0.0, "rear": round(WHEELBASE / B, 6),
                     "shape": "seraphhorizons:entity/handcar-axlebox"}
    rig["parts"] = parts
    return rig


def theta_of_frame(f):
    return TAU * RATIO * f / RIDER_FRAMES


# ---------------------------------------------------------------- the shape with joints and the pump animation
JOINTS = {   # part: (joint element name, pivot), the pitman's in two: a slide, then its swing
    "axle_front": ("af_joint", (X_FRONT, AXLE_Y, MID_Z)),
    "axle_rear": ("ar_joint", (X_REAR, AXLE_Y, MID_Z)),
    "gear": ("gr_joint", (COUNTER[0], COUNTER[1], MID_Z)),
    "beam": ("bm_joint", (PIVOT[0], PIVOT[1], MID_Z)),
    "pitman": ("pm_joint", (crank_pin(0.0)[0], crank_pin(0.0)[1], MID_Z)),
}


def rel_json(el, origin):
    """An element's JSON with its coordinates relative to `origin` (a joint's `from`)."""
    e = element_json(el)
    for key in ("from", "to", "rotationOrigin"):
        if key in e:
            e[key] = [riders.r4(e[key][k] - origin[k]) for k in range(3)]
    return e


def joint_json(name, at, children, rel_to=(0.0, 0.0, 0.0)):
    p = [riders.r4(at[k] - rel_to[k]) for k in range(3)]
    return {"name": name, "from": p, "to": p, "rotationOrigin": p, "faces": {}, "children": children}


def shape_tree(els, parts, aps):
    """The body: the frame's elements at the root, each moving part's under its joint."""
    roots = []
    by_part = {}
    for el in els:
        by_part.setdefault(part_of(parts, el.name), []).append(el)
    for el in by_part.get("frame", []):
        e = element_json(el)
        if el.name in aps:
            e["attachmentpoints"] = [{"code": a["code"], "posX": riders.r4(a["pos"][0]), "posY": riders.r4(a["pos"][1]),
                                      "posZ": riders.r4(a["pos"][2]), "rotationX": 0.0, "rotationY": 0.0, "rotationZ": 0.0}
                                     for a in aps[el.name]]
        roots.append(e)
    # the branch selector's three places: each lever with its knob as a child, so Yang's tesselation,
    # which removes the places not chosen by name (TNL_LFT, TNL_STR, TNL_RGT), takes the knob with it
    for pid in ("lever_left", "lever_straight", "lever_right"):
        handle, knob = sorted(by_part[pid], key=lambda el: len(el.name))
        roots.append(lever_json(handle, knob))
    # the cargo slot's mount: Yang's attachable puts a chest's shape here (stepParentTo)
    m = [riders.r4(v) for v in CARGO[0]]
    roots.append({"name": "cargo_mount", "from": m, "to": m, "faces": {}})
    for pid in ("axle_front", "axle_rear", "gear", "beam"):
        name, pivot = JOINTS[pid]
        roots.append(joint_json(name, pivot, [rel_json(el, pivot) for el in by_part[pid]]))
    name, pivot = JOINTS["pitman"]
    swing = joint_json("pm_swing", pivot, [rel_json(el, pivot) for el in by_part["pitman"]], rel_to=pivot)
    roots.append(joint_json(name, pivot, [swing]))
    # the handles' grips as attachment points on the beam, for the tests (the riders' hands go there)
    beam = next(r for r in roots if r["name"] == "bm_joint")
    beam["attachmentpoints"] = []
    for key in SEATS:
        for side, p in grip_points(key).items():
            rel = [riders.r4(p[k] - JOINTS["beam"][1][k]) for k in range(3)]
            beam["attachmentpoints"].append({"code": f"GRIP_{key.upper()}_{side}", "posX": rel[0], "posY": rel[1], "posZ": rel[2],
                                             "rotationX": 0.0, "rotationY": 0.0, "rotationZ": 0.0})
    return roots


def lever_json(handle, knob):
    """A lever and its knob, both turned about x through the selector's pivot, as a parent turned
    there with the knob in its frame."""
    _, ly, lz = LEVER_PIVOT
    tilt = math.degrees(math.atan2(handle.r[2][1], handle.r[1][1]))

    def unturned(el):
        a = -math.radians(tilt)
        y, z = el.c[1] - ly, el.c[2] - lz
        c = [el.c[0], ly + y * math.cos(a) - z * math.sin(a), lz + y * math.sin(a) + z * math.cos(a)]
        flat = El(el.name, el.size, c, [r[:] for r in IDENT], el.faces, el.part)
        return element_json(flat)
    h, k = unturned(handle), unturned(knob)
    for key in ("from", "to"):
        k[key] = [riders.r4(k[key][i] - h["from"][i]) for i in range(3)]
    if abs(tilt) > 1e-6:
        h["rotationOrigin"] = [riders.r4(h["from"][0]), riders.r4(ly), riders.r4(lz)]
        h["rotationX"] = riders.r4(tilt)
    h["children"] = [k]
    return h


def rotation_z_deg(m):
    return math.degrees(math.atan2(m[1][0], m[0][0]))


def body_animation(parts):
    """The car's "pump": a keyframe per frame for every joint, from the rig's own matrices."""
    keyframes = []
    for f in range(RIDER_FRAMES):
        th = theta_of_frame(f)
        els = {}
        for pid in ("axle_front", "axle_rear", "gear"):
            ang = rotation_z_deg(part_matrix(parts, pid, {"theta": th})) % 360.0
            els[JOINTS[pid][0]] = {"rotationX": 0.0, "rotationY": 0.0, "rotationZ": riders.r4(ang), "rotShortestDistanceZ": True}
        els["bm_joint"] = {"rotationX": 0.0, "rotationY": 0.0, "rotationZ": riders.r4(rotation_z_deg(part_matrix(parts, "beam", {"theta": th})))}
        m = part_matrix(parts, "pitman", {"theta": th})
        pivot = [v / B for v in JOINTS["pitman"][1]]
        moved = [m[i][0] * pivot[0] + m[i][1] * pivot[1] + m[i][2] * pivot[2] + m[i][3] for i in range(3)]
        d = [(moved[k] - pivot[k]) * B for k in range(3)]
        els["pm_joint"] = {"offsetX": riders.r4(d[0]), "offsetY": riders.r4(d[1]), "offsetZ": 0.0}
        els["pm_swing"] = {"rotationX": 0.0, "rotationY": 0.0, "rotationZ": riders.r4(rotation_z_deg(m))}
        keyframes.append({"frame": f, "elements": els})
    return {"name": "Pump", "code": BODY_ANIM, "quantityframes": RIDER_FRAMES, "onActivityStopped": "EaseOut",
            "onAnimationEnd": "Repeat", "keyframes": keyframes}


def textures_used(els):
    used = {f["texture"].lstrip("#") for el in els for f in el.faces.values()}
    return {k: v for k, v in TEXTURES.items() if k in used}


def shape_dumps_tree(head, roots, animations):
    lines = ["{"]
    for k, v in head.items():
        lines.append(f"\t{json.dumps(k)}: {json.dumps(v, separators=(', ', ': '))},")
    lines.append('\t"elements": [')
    lines.append(",\n".join("\t\t" + json.dumps(e, separators=(",", ":")) for e in roots))
    lines.append("\t]" + ("," if animations else ""))
    if animations:
        lines.append('\t"animations": [')
        chunks = []
        for a in animations:
            h = {k: v for k, v in a.items() if k != "keyframes"}
            inner = ",\n".join("\t\t\t" + json.dumps(kf, separators=(",", ":")) for kf in a["keyframes"])
            chunks.append("\t\t{" + json.dumps(h, separators=(", ", ": "))[1:-1] + ', "keyframes": [\n' + inner + "\n\t\t]}")
        lines.append(",\n".join(chunks))
        lines.append("\t]")
    lines.append("}")
    return "\n".join(lines) + "\n"


def body_shape(els, parts):
    head = {"_comment": f"Generated by {SCRIPT}; do not edit. The handcar's body for Yang's standard-gauge renderer: the frame at "
                        "the root, each moving part under its joint, and the pump animation that the Seraph Horizons mod runs "
                        "by the car's distance travelled. Made for the Seraph Horizons mod; no other mod's model is used.",
            "textureWidth": TEX, "textureHeight": TEX,
            "textureSizes": {k: [TEX, TEX] for k in textures_used(els)},
            "textures": textures_used(els)}
    return shape_dumps_tree(head, shape_tree(els, parts, attachment_points()), [body_animation(parts)])


def axlebox_shape():
    els = axlebox_elements()
    head = {"_comment": f"Generated by {SCRIPT}; do not edit. The handcar's bogie shape for Yang's renderer: one axle's journal "
                        "boxes, drawn where the bogie rides the track. Made for the Seraph Horizons mod.",
            "textureWidth": TEX, "textureHeight": TEX,
            "textureSizes": {k: [TEX, TEX] for k in textures_used(els)},
            "textures": textures_used(els)}
    return shape_dumps_tree(head, [element_json(el) for el in els], [])


# ---------------------------------------------------------------- the riders
def to_rider(seat_key, p):
    """A car-frame point (voxels) in the rider's model frame: the rider's feet (8, 0, 8) on the seat,
    turned by the seat's turn about y."""
    s = SEATS[seat_key]
    d = [p[k] - s["pos"][k] for k in range(3)]
    if s["turn"] == 180.0:
        d = [-d[0], d[1], -d[2]]
    return [d[0] + 8.0, d[1], d[2] + 8.0]


def grip_targets(parts, seat_key, frame):
    """A rider's two hand targets at a (fractional) frame, in the rider's frame."""
    m = part_matrix(parts, "beam", {"theta": theta_of_frame(frame)})
    out = {}
    for side, p in grip_points(seat_key).items():
        pb = [v / B for v in p]
        moved = [(m[i][0] * pb[0] + m[i][1] * pb[1] + m[i][2] * pb[2] + m[i][3]) * B for i in range(3)]
        out[side] = to_rider(seat_key, moved)
    return out


def lean_at(parts, seat_key, frame):
    """The pump's torso lean at a frame: forward as the rider's handle goes down."""
    m = part_matrix(parts, "beam", {"theta": theta_of_frame(frame)})
    x = SEATS[seat_key]["handle"]
    y = m[1][0] * x / B + m[1][1] * PIVOT[1] / B + m[1][3]
    travel = ARM * math.sin(BETA_SERIES[1][1])           # the first harmonic's height swing at the handle
    return -riders.LEAN_DEG * (y * B - PIVOT[1]) / max(travel, 1e-6)


def rider_animations(parts, skel):
    """Each seat's two seraph animations: the grip (upright) and the pump (leaning into the strokes),
    {seat: (grip, pump)}, solved against that seat's handle at every frame."""
    out = {}
    for key in SEATS:
        targets = [grip_targets(parts, key, f) for f in range(RIDER_FRAMES)]
        grip = riders.solve_cycle(skel, targets, [0.0] * RIDER_FRAMES)
        pump = riders.solve_cycle(skel, targets, [lean_at(parts, key, f) for f in range(RIDER_FRAMES)])
        out[key] = (riders.animation_json(grip_code(key), f"Seraph Horizons handcar grip ({key})", grip),
                    riders.animation_json(pump_code(key), f"Seraph Horizons handcar pump ({key})", pump))
    return out


def rider_meta(code, client_side):
    """The player's animation metadata: the grip is the seat's (the server starts it and tells every
    client), the pump the mod's own on each client."""
    meta = {"code": code, "animation": code, "animationSpeed": ANIM_SPEED, "easeInSpeed": ANIM_EASE, "easeOutSpeed": ANIM_EASE,
            "weight": RIDER_WEIGHT, "blendMode": "Average"}
    if client_side:
        meta["clientSide"] = True
    return meta


SERAPH_SHAPE = "game:shapes/entity/humanoid/seraph-faceless.json"
PLAYER_TYPE = "game:entities/humanoid/player.json"


def riders_patch(anims):
    ops = []
    for key in SEATS:
        for anim in anims[key]:
            ops.append({"op": "add", "path": "/animations/-", "file": SERAPH_SHAPE, "dependsOn": [{"modid": "yangtransport"}], "value": anim})
    for key in SEATS:
        ops.append({"op": "add", "path": "/client/animations/-", "file": PLAYER_TYPE, "side": "Server",
                    "dependsOn": [{"modid": "yangtransport"}], "value": rider_meta(grip_code(key), False)})
        ops.append({"op": "add", "path": "/client/animations/-", "file": PLAYER_TYPE, "side": "Server",
                    "dependsOn": [{"modid": "yangtransport"}], "value": rider_meta(pump_code(key), True)})
    return ops


SAMPLE_FRAMES = [0.0, 7.5, 15.0, 22.25, 30.0, 37.5, 45.0, 52.75, 59.5]


def rider_reference(parts, skel):
    poses = []
    for f in SAMPLE_FRAMES:
        for key in SEATS:
            tg = grip_targets(parts, key, f)
            poses.append({"seat": key, "frame": f, "theta": round(theta_of_frame(f), 9),
                          "right": [round(v, 5) for v in tg["R"]], "left": [round(v, 5) for v in tg["L"]]})
    return {"_comment": f"Generated by {SCRIPT}; do not edit. The riders' hand targets (their item anchors' attachment points, "
                        "voxels, in the rider's model frame) at sampled frames of the pump, which each seat's grip and pump "
                        "animations must reach (tests/PackTests runs the game's animator on the patched seraph against them), "
                        "and the seraph's numbers they were solved against.",
            "frames": RIDER_FRAMES, "tolerance": 0.25,
            "animations": {key: {"grip": grip_code(key), "pump": pump_code(key)} for key in SEATS},
            "seats": {key: {"pos": list(SEATS[key]["pos"]), "turn": SEATS[key]["turn"]} for key in SEATS},
            "skeleton": riders.skeleton_summary(skel), "poses": poses}


def reference(parts):
    poses = []
    for i in range(25):
        th = TAU * RATIO * i / 24 - 0.7
        poses.append({"theta": round(th, 9), "matrices": {p["id"]: round_matrix(part_matrix(parts, p["id"], {"theta": th})) for p in parts}})
    return {"_comment": f"Generated by {SCRIPT}; do not edit. Every handcar part's matrix (blocks, first three rows) at a spread of "
                        "axle angles, from the generator's reference maths (Machines/tools/machinegen/rigmath.py).",
            "poses": poses}


def dumps_rider_reference(ref):
    head = {k: v for k, v in ref.items() if k != "poses"}
    lines = ["{"]
    for k, v in head.items():
        lines.append(f"\t{json.dumps(k)}: {json.dumps(v, separators=(', ', ': '))},")
    lines.append('\t"poses": [')
    lines.append(",\n".join("\t\t" + json.dumps(p, separators=(",", ":")) for p in ref["poses"]))
    lines.append("\t]")
    lines.append("}")
    return "\n".join(lines) + "\n"


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--out", type=Path, help="write every file into this folder instead of the mod")
    ap.add_argument("--seraph", type=Path, help="the seraph's shape (default: VINTAGE_STORY's)")
    args = ap.parse_args()

    import validate_handcar  # noqa: E402
    parts = rig_parts()
    for p in parts:
        for d in p["drivers"]:
            validate_driver(d)
    els = build_all()
    validate_handcar.fix(els, parts, sys.modules[__name__])
    skel = riders.load_seraph(args.seraph)
    anims = rider_animations(parts, skel)
    body = body_shape(els, parts)
    problems = validate_handcar.validate(els, parts, skel, anims, json.loads(body), sys.modules[__name__])
    rig = rig_json(parts)
    files = {
        (SHAPE_DIR, "handcar.json"): body,
        (SHAPE_DIR, "handcar-axlebox.json"): axlebox_shape(),
        (RIG_DIR, "handcar-rig.json"): rig_dumps(rig, list_keys=("parts",)),
        (PATCH_DIR, "handcar-riders.json"): riders.dumps_patch(riders_patch(anims)),
        (TEST_DIR, "rig-reference.json"): reference_dumps(reference(parts)),
        (TEST_DIR, "rider-reference.json"): dumps_rider_reference(rider_reference(parts, skel)),
    }
    for (folder, name), text in files.items():
        json.loads(text)
        target = (args.out or folder) / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8")
    if problems:
        print(f"\n{len(problems)} check(s) failed:")
        for p in problems:
            print("  " + p)
        sys.exit(1)
    print("all checks passed")


if __name__ == "__main__":
    main()
