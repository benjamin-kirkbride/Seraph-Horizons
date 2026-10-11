#!/usr/bin/env python3
"""Write the rig drivers' cross-language fixture, `tests/Machines/driver-fixture.json`.

Every rig driver, old and new, evaluated by the reference maths (`machinegen/rigmath.py`) at a
deterministic grid of inputs, so the C# (`Machines/Core/RigAnimation.cs`, `DriverFixtureTests`) and
TypeScript (`site/src/lib/rig.ts`, `rig.test.ts`) implementations can be held to it before any
machine's model uses the new drivers. Run it from anywhere:

    python3 mods-src/seraphhorizons/Machines/tools/make_fixture.py [--out FILE]

Stdlib only; two runs write the same bytes. Never edit the output by hand.

The file (format 2):

    trunkPath    the trunk-flavoured progress every case in `drivers` and `rig` uses: nose(W) = nose0 + W,
                 tail = nose - lengths[class]
    drivers      [{id, driver, cases: [{inputs, matrix}]}]: one driver alone, its 4x4 matrix
    rig          {parts, poses: [{inputs, matrices: {part id: 4x4}}]}: a small synthetic rig that
                 uses every driver, with ride chains, composed as RigParts does
    invalid      [{id, driver, error}]: drivers every parser must reject (error is a hint only)
    work         the generic progress: a named quantity with a unit, a step and an end per class,
                 a point (nose = tail = W)
    workDrivers  as `drivers`, evaluated with `work` as the rig's progress
    workInvalid  [{id, driver, error}]: drivers every parser must reject with `work` (a roll)
    badProgress  [{id, rig, error}]: rig progress declarations every parser must reject
    stretchBoxes [{id, element, parts, part, inputs, authored, corners}]: a shape element (voxels, as a
                 shape file writes it) in a part of a small rig on `work`, posed: its 8 corners (corner i
                 takes `to` on x when bit 0 is set, y bit 1, z bit 2) authored and posed, voxels, as
                 machinegen's `posed` box gives them; a matrix applied to the authored corners must land
                 on them (the viewer's and the game's way), so the three agree on a stretched box

`inputs` always carries all nine keys (theta, depth, lifting, travel, work, size, presence,
feed, oil; size is the class 0 none / 1 thin / 2 thick / 3 (and on, to 9) a further class keyed by
its number; "work" is W, a trunk's travel on a trunkPath). Matrices are 4 rows of 4, block units,
row-major (row i, column j; translation in column 3), rounded to 9 places: compare within 1e-6.

Per-class values (amount, gain, lobes' amplitude, work's end) are keyed "thin", "thick", "3" .. "9";
a class a value does not name has none of it (amount and amplitude 0, gain 1, end 0). The cases with
class 3, and the drivers and parts that name it, come after the older ones in each list, so the older
cases keep their inputs. So do the stretching gauges (`"motion": "stretch"`) and the parts that use them.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from machinegen.geometry import flatten  # noqa: E402
from machinegen.rigmath import (apply, driver_matrix, full_inputs, part_matrix, posed, progress_of,  # noqa: E402
                                trunk_end, trunk_length, validate_driver, work_end)

OUT = Path(__file__).resolve().parents[2] / "tests" / "Machines" / "driver-fixture.json"

# Every number below is dyadic (exact in binary), so window edges land exactly on their edge in
# every language.
PATH = {"origin": [0.0, 1.6875, 0.5625], "axis": "x", "length": 14.0, "nose0": 0.5,
        "lengths": {"thin": 4, "thick": 5}, "tailStop": 9.0}
P1 = [0.3125, 1.6875, 0.5625]
P2 = [-0.75, 2.125, 1.25]
P3 = [1.5, 0.625, -0.4375]

W1 = {"from": 2.0, "to": 3.0, "ease": 0.5, "gain": {"thin": 1.0, "thick": 6.0}}
W2 = {"from": 5.0, "to": 8.0, "ease": 0.75}
WA = {"from": 3.0, "to": 6.0, "ease": 1.0, "gain": {"thin": 0.5}}        # thick's gain defaults to 1
WB = {"from": 4.0, "to": 5.0, "ease": 0.25, "gain": {"thick": 0.8}}      # thin's gain defaults to 1
LOBES = {"ratio": 4.0, "phase": 0.25, "amplitude": {"thin": 0.02, "thick": 0.05}}

DRIVERS = [
    # the mill's drivers, as it writes them
    ("rotate_theta", {"type": "rotate", "axis": "x", "pivot": P1, "ratio": 1.0}),
    ("rotate_theta_z", {"type": "rotate", "axis": "z", "pivot": P2, "ratio": -0.42}),
    ("rotate_rectified", {"type": "rotate", "axis": "y", "pivot": P3, "ratio": -1.3, "rectified": True}),
    ("slide_theta", {"type": "slide", "axis": "z", "amplitude": 0.12, "ratio": 2.0, "phase": 0.4}),
    ("swing_theta", {"type": "swing", "axis": "x", "pivot": P1, "amplitude": 0.3, "ratio": 1.0, "phase": -1.1}),
    ("feed", {"type": "feed", "axis": "y", "travel": -2.125}),
    ("step_rotate", {"type": "step", "motion": "rotate", "axis": "z", "pivot": P2, "amount": 1.5, "from": 0.2, "to": 0.8}),
    ("step_hold", {"type": "step", "motion": "slide", "axis": "x", "amount": 0.25, "from": 0.5, "to": 1.0, "lifting": "hold"}),
    ("step_block", {"type": "step", "motion": "slide", "axis": "y", "amount": -0.4, "from": 0.0, "to": 0.5, "lifting": "block"}),
    ("step_trip", {"type": "step", "motion": "rotate", "axis": "x", "pivot": P3, "amount": -0.6, "from": 0.9375, "to": 1.0,
                   "lifting": "trip", "top": 0.0625}),
    ("stretch", {"type": "stretch", "axis": "y", "anchor": P1, "length": -0.8125, "travel": -2.0}),
    ("stretch_input_depth", {"type": "stretch", "axis": "x", "anchor": P2, "length": 0.25, "travel": 1.5, "input": "depth"}),
    ("stretch_input_oil", {"type": "stretch", "axis": "y", "anchor": P3, "length": 0.0625, "travel": 0.125, "input": "oil"}),
    # `input` on rotate, slide and swing
    ("rotate_input_theta", {"type": "rotate", "axis": "y", "pivot": P1, "ratio": 0.6, "input": "theta"}),
    ("rotate_input_travel", {"type": "rotate", "axis": "y", "pivot": P3, "ratio": -1.3, "input": "travel"}),
    ("rotate_input_feed", {"type": "rotate", "axis": "x", "pivot": P2, "ratio": 0.37, "input": "feed"}),
    ("rotate_input_trunk", {"type": "rotate", "axis": "z", "pivot": P1, "ratio": -1.9, "input": "trunk"}),
    ("slide_input_travel", {"type": "slide", "axis": "x", "amplitude": 0.0625, "ratio": 0.5, "phase": 1.0, "input": "travel"}),
    ("slide_input_feed", {"type": "slide", "axis": "y", "amplitude": 0.05, "ratio": 3.0, "phase": 0.2, "input": "feed"}),
    ("slide_input_trunk", {"type": "slide", "axis": "x", "amplitude": 0.2, "ratio": 1.5, "input": "trunk"}),
    ("swing_input_travel", {"type": "swing", "axis": "z", "pivot": P2, "amplitude": 0.25, "ratio": 0.5, "phase": 0.3, "input": "travel"}),
    ("swing_input_feed", {"type": "swing", "axis": "y", "pivot": P3, "amplitude": 0.4, "ratio": 2.0, "input": "feed"}),
    ("swing_input_trunk", {"type": "swing", "axis": "x", "pivot": P1, "amplitude": 0.15, "ratio": 0.75, "phase": -0.5, "input": "trunk"}),
    ("rotate_input_oil", {"type": "rotate", "axis": "x", "pivot": P3, "ratio": 2.25, "input": "oil"}),
    ("slide_input_oil", {"type": "slide", "axis": "z", "amplitude": 0.3, "ratio": 1.25, "phase": 0.1, "input": "oil"}),
    # gauge
    ("gauge_occupy_rotate_lobes", {"type": "gauge", "motion": "rotate", "axis": "x", "pivot": P1,
                                   "amount": {"thin": 0.35, "thick": -0.2}, "windows": [W1, W2], "lobes": LOBES}),
    ("gauge_occupy_slide", {"type": "gauge", "motion": "slide", "axis": "y", "amount": {"thin": 0.47, "thick": 0.0}, "windows": [W1]}),
    ("gauge_occupy_overlap", {"type": "gauge", "motion": "slide", "axis": "z", "mode": "occupy",
                              "amount": {"thin": -0.3125, "thick": 0.5}, "windows": [WA, WB]}),
    ("gauge_occupy_rotate", {"type": "gauge", "motion": "rotate", "axis": "y", "pivot": P2,
                             "amount": {"thin": 0.8, "thick": 1.1}, "windows": [W2]}),
    ("gauge_present_slide", {"type": "gauge", "motion": "slide", "axis": "y", "mode": "present", "amount": {"thin": 0.1, "thick": 0.3}}),
    ("gauge_present_rotate_lobes", {"type": "gauge", "motion": "rotate", "axis": "z", "pivot": P3, "mode": "present",
                                    "amount": {"thin": -0.25, "thick": 0.4}, "lobes": {"ratio": -2.0, "amplitude": {"thin": 0.1, "thick": 0.0}}}),
    # roll
    ("roll_z", {"type": "roll", "axis": "z", "pivot": P2, "at": 4.0, "ratio": 2.5}),
    ("roll_x", {"type": "roll", "axis": "x", "pivot": P3, "at": 9.5, "ratio": -1.2}),
    # `input` "work", the generic spelling of "trunk"
    ("rotate_input_work", {"type": "rotate", "axis": "z", "pivot": P2, "ratio": 0.8125, "input": "work"}),
    ("slide_input_work", {"type": "slide", "axis": "y", "amplitude": 0.125, "ratio": 2.5, "phase": 0.5, "input": "work"}),
    # classes past thick, and per-class values that name only some classes (the others have none)
    ("gauge_occupy_thin_only", {"type": "gauge", "motion": "slide", "axis": "x", "amount": {"thin": 0.4375}, "windows": [W1]}),
    ("gauge_occupy_class3", {"type": "gauge", "motion": "rotate", "axis": "y", "pivot": P3, "amount": {"thin": 0.25, "3": -0.625},
                             "windows": [W1, {"from": 5.0, "to": 8.0, "ease": 0.75, "gain": {"3": 2.0}}]}),
    ("gauge_present_class3_lobes", {"type": "gauge", "motion": "rotate", "axis": "z", "pivot": P1, "mode": "present",
                                    "amount": {"3": 0.5}, "lobes": {"ratio": 3.0, "phase": 0.5, "amplitude": {"3": 0.0625}}}),
    ("gauge_present_class9", {"type": "gauge", "motion": "slide", "axis": "z", "mode": "present", "amount": {"thick": 0.125, "9": -0.25}}),
    # stretching gauges: windows that add up (W1 and W2 overlap nowhere, WA and WB do), an axis in a turned
    # frame, and thin's factor reaching 0 (the part collapses to its origin)
    ("gauge_stretch_occupy", {"type": "gauge", "motion": "stretch", "axis": "y", "origin": P1,
                              "amount": {"thin": 0.5, "thick": -0.75}, "windows": [W1, W2]}),
    ("gauge_stretch_overlap", {"type": "gauge", "motion": "stretch", "axis": "x", "origin": P3,
                               "amount": {"thin": -0.375, "thick": 0.25, "3": -0.5}, "windows": [WA, WB]}),
    ("gauge_stretch_turned_present", {"type": "gauge", "motion": "stretch", "axis": "z", "origin": P2, "rotation": [30.0, -45.0, 10.0],
                                      "mode": "present", "amount": {"thin": -1.0, "3": 0.5}}),
]

# The generic progress: a quantity counted in its own unit (parts made, say), a point on its own
# scale. Its drivers' windows are where W is, with nothing behind it.
WORK = {"name": "parts made", "unit": "parts", "step": 0.25, "end": {"thin": 6.0, "thick": 9.0, "3": 4.0}}
WORK_DRIVERS = [
    ("work_gauge_occupy_rotate_lobes", {"type": "gauge", "motion": "rotate", "axis": "x", "pivot": P1,
                                        "amount": {"thin": 0.35, "thick": -0.2}, "windows": [W1, W2], "lobes": LOBES}),
    ("work_gauge_occupy_overlap", {"type": "gauge", "motion": "slide", "axis": "z", "amount": {"thin": -0.3125, "thick": 0.5},
                                   "windows": [WA, WB]}),
    ("work_gauge_staircase", {"type": "gauge", "motion": "rotate", "axis": "y", "pivot": P3, "amount": {"thin": -0.5, "thick": -0.3},
                              "windows": [{"from": 1.5, "to": 1000.0, "ease": 0.25, "gain": {"thin": 1.0, "thick": 0.0}},
                                          {"from": 2.5, "to": 1000.0, "ease": 0.5, "gain": {"thin": 0.0, "thick": 1.0}}]}),
    ("work_gauge_present", {"type": "gauge", "motion": "slide", "axis": "y", "mode": "present", "amount": {"thin": 0.1, "thick": 0.3}}),
    ("work_rotate_input_work", {"type": "rotate", "axis": "z", "pivot": P1, "ratio": -1.9, "input": "work"}),
    ("work_slide_input_trunk", {"type": "slide", "axis": "x", "amplitude": 0.2, "ratio": 1.5, "input": "trunk"}),
    # a part that moves only for class 3, in quarters of its work (the mandrel station's spring's kind)
    ("work_gauge_class3_only", {"type": "gauge", "motion": "rotate", "axis": "x", "pivot": P2, "amount": {"3": 1.25},
                                "windows": [{"from": 1.0, "to": 1000.0, "ease": 1.0}, {"from": 2.5, "to": 3.5, "ease": 0.5, "gain": {"3": 0.5}}]}),
    # a straight rod shrinking in four steps to nothing (a quarter a window, so the windows add up to 1), along
    # the z of a frame turned about z, for class 3 only
    ("work_gauge_stretch_steps", {"type": "gauge", "motion": "stretch", "axis": "z", "origin": P3, "rotation": [0.0, 0.0, -20.0],
                                  "amount": {"3": -1.0},
                                  "windows": [{"from": 0.5, "to": 1000.0, "ease": 0.5, "gain": {"3": 0.25}},
                                              {"from": 1.5, "to": 1000.0, "ease": 0.5, "gain": {"3": 0.25}},
                                              {"from": 2.5, "to": 1000.0, "ease": 0.5, "gain": {"3": 0.25}},
                                              {"from": 3.0, "to": 1000.0, "ease": 0.75, "gain": {"3": 0.25}}]}),
    # a coil's side growing from nothing at its corner: vanished until W 1, its whole length by W 2 (class 3); thin
    # grows by half over the same window and thick does not move
    ("work_gauge_stretch_grow", {"type": "gauge", "motion": "stretch", "axis": "z", "origin": P1, "rotation": [-17.5, 0.0, 0.0],
                                 "amount": {"thin": 0.5, "3": -1.0}, "windows": [{"from": -1000.0, "to": 2.0, "ease": 1.0}]}),
    # past nothing: a factor that would go below 0 is held at 0
    ("work_gauge_stretch_clamped", {"type": "gauge", "motion": "stretch", "axis": "y", "origin": P2,
                                    "amount": {"thick": -1.5}, "windows": [{"from": 1.0, "to": 1000.0, "ease": 4.0}]}),
]
WORK_INVALID = [
    ("roll_on_work", {"type": "roll", "axis": "z", "pivot": P2, "at": 4.0, "ratio": 2.5}, "a roll needs a trunkPath"),
]
BAD_PROGRESS = [
    ("both", {"trunkPath": PATH, "work": WORK}, "work and a trunkPath"),
    ("no_unit", {"work": {"end": {"thin": 1.0, "thick": 2.0}}}, "no unit"),
    ("end_no_class", {"work": {"unit": "parts", "end": {}}}, "end names no class"),
    ("end_not_object", {"work": {"unit": "parts", "end": 3.0}}, "end not an object keyed by class"),
    ("end_unknown_class", {"work": {"unit": "parts", "end": {"thin": 1.0, "medium": 2.0}}}, "end names a key that is no class"),
    ("end_class_by_number", {"work": {"unit": "parts", "end": {"1": 1.0}}}, "thin is \"thin\", not \"1\""),
    ("end_class_too_high", {"work": {"unit": "parts", "end": {"10": 1.0}}}, "a class past 9"),
    ("end_zero", {"work": {"unit": "parts", "end": {"thin": 0.0, "thick": 2.0}}}, "end not above 0"),
    ("end_class3_zero", {"work": {"unit": "parts", "end": {"thin": 1.0, "3": 0.0}}}, "end not above 0 for class 3"),
    ("step_zero", {"work": {"unit": "parts", "step": 0.0, "end": {"thin": 1.0, "thick": 2.0}}}, "step not above 0"),
    ("trunk_keys", {"work": {"unit": "parts", "end": {"thin": 1.0, "thick": 2.0}, "lengths": {"thin": 1.0, "thick": 1.0}}},
     "a trunkPath's key in work"),
]

INVALID = [
    ("input_and_rectified", {"type": "rotate", "axis": "x", "pivot": P1, "ratio": 1.0, "rectified": True, "input": "feed"},
     "both 'input' and 'rectified'"),
    ("input_and_rectified_false", {"type": "swing", "axis": "x", "pivot": P1, "amplitude": 0.1, "rectified": False, "input": "theta"},
     "both 'input' and 'rectified', whatever their values"),
    ("input_unknown", {"type": "slide", "axis": "y", "amplitude": 0.1, "input": "depth"}, "unknown input"),
    ("stretch_input_unknown", {"type": "stretch", "axis": "y", "anchor": P1, "length": 0.5, "travel": 1.0, "input": "theta"},
     "a stretch reads depth or oil only"),
    ("gauge_ease_zero", {"type": "gauge", "motion": "slide", "axis": "y", "amount": {"thin": 1.0, "thick": 1.0},
                         "windows": [{"from": 1.0, "to": 2.0, "ease": 0.0}]}, "ease <= 0"),
    ("gauge_ease_negative", {"type": "gauge", "motion": "slide", "axis": "y", "amount": {"thin": 1.0, "thick": 1.0},
                             "windows": [W1, {"from": 1.0, "to": 2.0, "ease": -0.5}]}, "ease <= 0"),
    ("gauge_to_not_past_from", {"type": "gauge", "motion": "slide", "axis": "y", "amount": {"thin": 1.0, "thick": 1.0},
                                "windows": [{"from": 2.0, "to": 2.0, "ease": 0.5}]}, "to <= from"),
    ("gauge_amount_empty", {"type": "gauge", "motion": "slide", "axis": "y", "amount": {}, "windows": [W1]}, "amount names no class"),
    ("gauge_amount_unknown_class", {"type": "gauge", "motion": "slide", "axis": "y", "amount": {"thin": 1.0, "medium": 1.0},
                                    "windows": [W1]}, "a key that is no class"),
    ("gauge_amount_class_by_number", {"type": "gauge", "motion": "slide", "axis": "y", "mode": "present", "amount": {"2": 1.0}},
     "thick is \"thick\", not \"2\""),
    ("gauge_amount_class_zero", {"type": "gauge", "motion": "slide", "axis": "y", "mode": "present", "amount": {"0": 1.0}},
     "0 is none, not a class"),
    ("gauge_amount_class_too_high", {"type": "gauge", "motion": "slide", "axis": "y", "mode": "present", "amount": {"10": 1.0}},
     "a class past 9"),
    ("gauge_amount_class_padded", {"type": "gauge", "motion": "slide", "axis": "y", "mode": "present", "amount": {"03": 1.0}},
     "class 3 is \"3\", not \"03\""),
    ("gauge_amount_not_number", {"type": "gauge", "motion": "slide", "axis": "y", "mode": "present", "amount": {"thin": "1"}},
     "an amount that is not a number"),
    ("gauge_amount_not_object", {"type": "gauge", "motion": "slide", "axis": "y", "mode": "present", "amount": 1.0},
     "a gauge's amount is per class"),
    ("gauge_gain_unknown_class", {"type": "gauge", "motion": "slide", "axis": "y", "amount": {"thin": 1.0},
                                  "windows": [{"from": 1.0, "to": 2.0, "ease": 0.5, "gain": {"huge": 1.0}}]}, "a gain keyed by no class"),
    ("gauge_lobes_amplitude_empty", {"type": "gauge", "motion": "rotate", "axis": "x", "pivot": P1, "mode": "present",
                                     "amount": {"3": 1.0}, "lobes": {"ratio": 1.0, "amplitude": {}}}, "lobes' amplitude names no class"),
    ("gauge_amount_missing", {"type": "gauge", "motion": "slide", "axis": "y", "mode": "present"}, "no amount"),
    ("gauge_occupy_no_windows", {"type": "gauge", "motion": "slide", "axis": "y", "amount": {"thin": 1.0, "thick": 1.0}},
     "occupy (the default) without windows"),
    ("gauge_occupy_empty_windows", {"type": "gauge", "motion": "slide", "axis": "y", "mode": "occupy",
                                    "amount": {"thin": 1.0, "thick": 1.0}, "windows": []}, "occupy with no windows"),
    ("gauge_lobes_on_slide", {"type": "gauge", "motion": "slide", "axis": "y", "amount": {"thin": 1.0, "thick": 1.0},
                              "windows": [W1], "lobes": LOBES}, "lobes on a slide"),
    ("gauge_lobes_no_ratio", {"type": "gauge", "motion": "rotate", "axis": "x", "pivot": P1, "amount": {"thin": 1.0, "thick": 1.0},
                              "windows": [W1], "lobes": {"amplitude": {"thin": 0.1, "thick": 0.1}}}, "lobes without a ratio"),
    ("gauge_motion_unknown", {"type": "gauge", "motion": "swing", "axis": "x", "pivot": P1, "amount": {"thin": 1.0, "thick": 1.0},
                              "windows": [W1]}, "motion not slide or rotate"),
    ("gauge_mode_unknown", {"type": "gauge", "motion": "slide", "axis": "y", "mode": "touch", "amount": {"thin": 1.0, "thick": 1.0},
                            "windows": [W1]}, "unknown mode"),
    ("gauge_rotate_no_pivot", {"type": "gauge", "motion": "rotate", "axis": "x", "amount": {"thin": 1.0, "thick": 1.0},
                               "windows": [W1]}, "a rotating gauge without a pivot"),
    ("roll_no_at", {"type": "roll", "axis": "z", "pivot": P2, "ratio": 2.5}, "roll without 'at'"),
    ("gauge_stretch_no_origin", {"type": "gauge", "motion": "stretch", "axis": "z", "amount": {"3": -1.0}, "windows": [W1]},
     "a stretching gauge without an origin"),
    ("gauge_stretch_short_origin", {"type": "gauge", "motion": "stretch", "axis": "z", "origin": [0.5, 1.0], "mode": "present",
                                    "amount": {"3": -1.0}}, "an origin of 2 numbers"),
    ("gauge_stretch_pivot_not_origin", {"type": "gauge", "motion": "stretch", "axis": "z", "pivot": P1, "mode": "present",
                                        "amount": {"3": -1.0}}, "a stretch's fixed point is its origin, not a pivot"),
    ("gauge_stretch_bad_rotation", {"type": "gauge", "motion": "stretch", "axis": "z", "origin": P1, "rotation": [0.0, 90.0],
                                    "mode": "present", "amount": {"3": -1.0}}, "a rotation of 2 numbers"),
    ("gauge_stretch_rotation_not_numbers", {"type": "gauge", "motion": "stretch", "axis": "z", "origin": P1, "rotation": ["x", 0.0, 0.0],
                                            "mode": "present", "amount": {"3": -1.0}}, "a rotation that is not numbers"),
    ("gauge_stretch_bad_axis", {"type": "gauge", "motion": "stretch", "axis": "w", "origin": P1, "mode": "present",
                                "amount": {"3": -1.0}}, "an axis that is not x, y or z"),
    ("gauge_stretch_lobes", {"type": "gauge", "motion": "stretch", "axis": "x", "origin": P1, "amount": {"thin": 1.0},
                             "windows": [W1], "lobes": LOBES}, "lobes on a stretch"),
    ("gauge_stretch_no_windows", {"type": "gauge", "motion": "stretch", "axis": "x", "origin": P1, "amount": {"thin": 1.0}},
     "an occupy stretch without windows"),
    ("gauge_stretch_amount_not_per_class", {"type": "gauge", "motion": "stretch", "axis": "x", "origin": P1, "mode": "present",
                                            "amount": 0.5}, "a stretch's amount is per class"),
    ("step_motion_stretch", {"type": "step", "motion": "stretch", "axis": "y", "amount": 0.5, "from": 0.0, "to": 1.0},
     "only a gauge stretches"),
]

# The synthetic rig: a ring turning on psi, arms on it closing on the trunk (with lobes), tips on
# the arms; a cradle weighed down by the trunk with an idle roll on it; a top-roll arm lifted by
# the trunk, its roll turned by the feed; a rock held by either of two contacts; a worm wheel slid
# by a gauge and turned by the feed; and the mill's kinds (theta, rectified, step trip, stretch).
RIG_PARTS = [
    {"id": "shaft", "match": ["shaft_*"], "requires": "shaft", "drivers": [{"type": "rotate", "axis": "x", "pivot": P1, "ratio": 1.0}]},
    {"id": "ring", "match": ["ring_*"], "requires": "ring",
     "drivers": [{"type": "rotate", "axis": "x", "pivot": P2, "ratio": 0.4375, "input": "travel"}]},
    {"id": "arm1", "match": ["arm1_*"], "requires": "ring", "ride": "ring",
     "drivers": [{"type": "gauge", "motion": "rotate", "axis": "z", "pivot": P3, "amount": {"thin": 0.6, "thick": 0.25},
                  "windows": [{"from": 6.0, "to": 6.5, "ease": 0.375}], "lobes": {"ratio": 4.0, "phase": 0.0, "amplitude": {"thin": 0.01, "thick": 0.04}}}]},
    {"id": "tip1", "match": ["tip1_*"], "requires": "heads", "ride": "arm1",
     "drivers": [{"type": "gauge", "motion": "slide", "axis": "y", "mode": "present", "amount": {"thin": -0.0625, "thick": 0.03125}}]},
    {"id": "cradle", "match": ["cradle_*"], "requires": None,
     "drivers": [{"type": "gauge", "motion": "slide", "axis": "y", "amount": {"thin": 0.0, "thick": -0.46875},
                  "windows": [{"from": 1.0, "to": 4.0, "ease": 0.5, "gain": {"thin": 1.0, "thick": 4.0}}]}]},
    {"id": "botroll", "match": ["botroll_*"], "requires": "rollsin", "ride": "cradle",
     "drivers": [{"type": "roll", "axis": "z", "pivot": P1, "at": 2.5, "ratio": -3.2}]},
    {"id": "toparm", "match": ["toparm_*"], "requires": "rollsin",
     "drivers": [{"type": "gauge", "motion": "rotate", "axis": "z", "pivot": P2, "amount": {"thin": 0.125, "thick": 0.5},
                  "windows": [{"from": 2.5, "to": 3.25, "ease": 0.25}]}]},
    {"id": "toproll", "match": ["toproll_*"], "requires": "rollsin", "ride": "toparm",
     "drivers": [{"type": "rotate", "axis": "z", "pivot": P1, "ratio": 1.75, "input": "feed"}]},
    {"id": "rock", "match": ["rock_*"], "requires": "levers",
     "drivers": [{"type": "gauge", "motion": "rotate", "axis": "x", "pivot": P3, "amount": {"thin": -0.35, "thick": -0.35},
                  "windows": [{"from": 0.75, "to": 1.25, "ease": 0.5, "gain": {"thin": 1.0, "thick": 6.0}},
                              {"from": 2.5, "to": 3.25, "ease": 0.25}, {"from": 10.0, "to": 10.75, "ease": 0.25}]}]},
    {"id": "wheel", "match": ["wheel_*"], "requires": "rollsin",
     "drivers": [{"type": "gauge", "motion": "slide", "axis": "z", "mode": "present", "amount": {"thin": 0.0625, "thick": 0.0625}},
                 {"type": "rotate", "axis": "z", "pivot": P2, "ratio": -0.05, "input": "feed"}]},
    {"id": "pinion", "match": ["pinion_*"], "requires": "shaft",
     "drivers": [{"type": "rotate", "axis": "x", "pivot": P1, "ratio": -1.0, "rectified": True}]},
    {"id": "yoke", "match": ["yoke_*"], "requires": "shaft",
     "drivers": [{"type": "slide", "axis": "z", "amplitude": 0.11, "ratio": 1.0, "phase": 0.7}]},
    {"id": "rod", "match": ["rod_*"], "requires": "shaft", "ride": "yoke",
     "drivers": [{"type": "swing", "axis": "x", "pivot": P2, "amplitude": 0.2, "ratio": 1.0, "phase": -0.3}]},
    {"id": "carriage", "match": ["carriage_*"], "requires": "sash", "drivers": [{"type": "feed", "axis": "y", "travel": -2.125}]},
    {"id": "rope", "match": ["rope*"], "requires": "sash",
     "drivers": [{"type": "stretch", "axis": "y", "anchor": P3, "length": -0.5625, "travel": -2.125}]},
    {"id": "level", "match": ["level_*"], "requires": None, "ride": "carriage",
     "drivers": [{"type": "stretch", "axis": "y", "anchor": P1, "length": 0.0625, "travel": 0.1875, "input": "oil"}]},
    {"id": "trip", "match": ["trip_*"], "requires": "levers",
     "drivers": [{"type": "step", "motion": "rotate", "axis": "x", "pivot": P1, "amount": -0.5, "from": 0.9375, "to": 1.0,
                  "lifting": "trip", "top": 0.0625}]},
    # a coil that winds only for class 3, riding the ring, and a link riding it that turns back with it (a chain of rides)
    {"id": "coil", "match": ["coil_*"], "requires": "heads", "ride": "ring",
     "drivers": [{"type": "gauge", "motion": "rotate", "axis": "y", "pivot": P1, "amount": {"3": 1.5},
                  "windows": [{"from": 1.0, "to": 1000.0, "ease": 2.0}]}]},
    {"id": "link", "match": ["link_*"], "requires": "heads", "ride": "coil",
     "drivers": [{"type": "gauge", "motion": "rotate", "axis": "y", "pivot": P3, "amount": {"3": -1.5},
                  "windows": [{"from": 1.0, "to": 1000.0, "ease": 2.0}]},
                 {"type": "gauge", "motion": "slide", "axis": "z", "mode": "present", "amount": {"thin": 0.0625, "3": 0.25}}]},
    # a jib turning on theta; a boom riding it, stretched along its own (turned) length from its foot, and
    # collapsing for class 3 once both its windows are full (they add up past 1); a hook and a cap riding the
    # boom, which move and turn with it but are never stretched by it
    {"id": "jib", "match": ["jib_*"], "requires": None, "drivers": [{"type": "rotate", "axis": "z", "pivot": P2, "ratio": 0.5}]},
    {"id": "boom", "match": ["boom_*"], "requires": None, "ride": "jib",
     "drivers": [{"type": "gauge", "motion": "stretch", "axis": "x", "origin": P1, "rotation": [0.0, 0.0, 25.0],
                  "amount": {"thin": 0.75, "thick": -0.5, "3": -1.0},
                  "windows": [{"from": 1.0, "to": 1000.0, "ease": 2.0}, {"from": 5.0, "to": 1000.0, "ease": 1.0, "gain": {"thick": 0.5}}]},
                 {"type": "gauge", "motion": "slide", "axis": "y", "mode": "present", "amount": {"thin": 0.125, "thick": 0.125, "3": 0.125}}]},
    {"id": "hook", "match": ["hook_*"], "requires": None, "ride": "boom",
     "drivers": [{"type": "swing", "axis": "x", "pivot": P3, "amplitude": 0.25, "ratio": 1.0}]},
    {"id": "cap", "match": ["cap_*"], "requires": None, "ride": "boom", "drivers": []},
    {"id": "frame", "match": ["*"], "requires": None, "drivers": []},
]
CLASS_SWEEP = (3, 9, 10)                     # class 3 and the last class; 10 is past the last, so none


def r9(v):
    v = round(float(v), 9)
    return 0.0 if v == 0 else v


def r6(v):
    v = round(v, 6)
    return 0.0 if v == 0 else v


def mat(m):
    return [[r9(v) for v in row] for row in m]


def pose(i, trunk, size, presence, depth=None, lifting=None):
    """A full set of inputs; the clocks (theta, travel, feed) and the mill's inputs vary with the
    case's index i, so every case also exercises them."""
    theta = r6(math.remainder(1.37 * i - 2.0, 2 * math.pi) * (1 if i % 3 else -1))
    return {"theta": theta, "depth": r6((i * 0.173) % 1.0) if depth is None else depth,
            "lifting": float(i % 2) if lifting is None else lifting, "travel": r6(abs(theta) + 0.61 * i),
            "work": trunk, "size": size, "presence": presence, "feed": r6(0.43 * i), "oil": r6((0.29 * i + 0.1) % 1.0)}


def general():
    """Every kind of input at once, for the drivers that do not read the trunk."""
    out = []
    for i in range(16):
        out.append(pose(i, r6(i * 0.8125), i % 3, (1.0, 0.3, 1.0, 0.0)[i % 4]))
    out += [pose(16, 0.0, 0, 0.0, 0.0, 0.0), pose(17, 3.0, 2, 1.0, 1.0, 1.0), pose(18, 7.0, 1, 1.0, 0.95, 0.5),
            pose(19, 0.25, 1, 0.3, 0.03, 1.0), pose(20, 9.0, 2, 0.0, 0.5, 0.25)]
    for p in out[-5:]:
        p["theta"] = -p["theta"]                 # negative angles, so rectified and theta differ
    # the classes past thick, after the older cases (clocks seeded from small indices: a float32 matrix keeps
    # 1e-6 only while the shaft's travel stays modest)
    out += [pose(2 + j, r6(1.5 + 2.25 * j), k, (1.0, 0.3)[j % 2]) for j, k in enumerate(CLASS_SWEEP)]
    return out


def trunk_cases(d, path=None):
    """The work sweep for a gauge or a roll: each window's (or the roll's) edges and mid-ramps for
    both classes, the start and the end of the trip, all three classes at every one of those
    travels, and presence 0 and 0.3 where a contact is full. `path` is the progress (PATH by default)."""
    path = PATH if path is None else path
    nose0 = path.get("nose0", 0.0)
    ts = {0.0}
    full = []
    for k in (1, 2):
        ln = trunk_length(path, k)
        ts.add(trunk_end(path, k))
        for w in d.get("windows", []) if d.get("mode", "occupy") == "occupy" else []:
            for nose_at in (w["from"], w["from"] + w["ease"] / 2, w["from"] + w["ease"]):
                ts.add(nose_at - nose0)
            for tail_at in (w["to"] - w["ease"], w["to"] - w["ease"] / 2, w["to"]):
                if tail_at + ln - nose0 < 100:
                    ts.add(tail_at + ln - nose0)
            full.append((k, ((w["from"] + w["ease"] + w["to"]) / 2 if w["to"] < 100 else w["from"] + 2 * w["ease"]) - nose0))
        if d["type"] == "roll":
            for nose_at in (d["at"] - 0.5, d["at"], d["at"] + ln / 2, d["at"] + ln, d["at"] + ln + 1.0):
                ts.add(nose_at - nose0)
            full.append((k, d["at"] + ln / 2 - nose0))
        if d.get("mode") == "present":
            full.append((k, 3.0))
    out = []
    i = 0
    for t in sorted(ts):
        for k in (0, 1, 2):
            out.append(pose(i, t, k, 1.0))
            i += 1
    for k, t in full:
        for p in (0.0, 0.3):
            out.append(pose(i, t, k, p))
            i += 1
    # the classes past thick, after the older cases: the same travels and the class's own end, full and eased
    ts3 = set(ts) | {work_end(path, k) for k in CLASS_SWEEP}
    for t in sorted(ts3):
        for k in CLASS_SWEEP:
            out.append(pose(i, t, k, 1.0))
            i += 1
    for w in d.get("windows", []) if d.get("mode", "occupy") == "occupy" else []:
        t = ((w["from"] + w["ease"] + w["to"]) / 2 if w["to"] < 100 else w["from"] + 2 * w["ease"]) - nose0
        for k in CLASS_SWEEP[:2]:
            for p in (0.0, 0.3):
                out.append(pose(i, t, k, p))
                i += 1
    return out


def rig_poses():
    """About 40 poses for the synthetic rig, across its windows' edges, every class and presence."""
    plan = [(0.0, 0, 0.0), (5.0, 0, 0.0), (5.0, 0, 0.3), (0.0, 1, 0.0), (0.0, 1, 0.3), (0.0, 1, 1.0), (0.0, 2, 1.0)]
    for k in (1, 2):
        for t in (0.25, 0.5, 0.75, 1.0, 1.5, 2.0, 2.25, 2.75, 3.5, 5.5, 5.75, 6.0, 7.5):
            plan.append((t, k, 1.0))
        plan.append((trunk_end(PATH, k), k, 1.0))
        plan.append((trunk_end(PATH, k) - 0.125, k, 0.3))
    plan += [(2.75, 1, 0.3), (2.75, 2, 0.0), (6.0, 2, 0.3), (1.0, 2, 0.3)]
    # the classes past thick, after the older poses
    plan += [(t, 3, 1.0) for t in (0.0, 1.0, 2.0, 2.75, 3.0, 6.0)] + [(2.0, 3, 0.3), (2.0, 9, 1.0), (2.0, 10, 1.0)]
    # the stretched boom: class 3 shrinking to nothing and past it, thick's second window, thin grown
    plan += [(t, 3, 1.0) for t in (1.5, 2.5, 4.0, 5.0)] + [(t, 2, 1.0) for t in (4.5, 5.5, 8.0)] + [(8.0, 1, 0.5)]
    return [pose(i, t, k, p) for i, (t, k, p) in enumerate(plan)]


# Shape elements posed in a small rig on WORK: the posed box's corners, held to the matrices. A coil side
# (a bar turned about x through its start corner) grown from that corner along its own length, a box
# riding a stretched part, a straight rod collapsed to nothing, and the side riding a rotated part.
SIDE = {"name": "side", "from": [4.0, 20.0, 8.0], "to": [5.0, 21.0, 16.0], "rotationOrigin": [4.0, 20.0, 8.0], "rotationX": -17.5}
GROW = {"type": "gauge", "motion": "stretch", "axis": "z", "origin": [0.25, 1.25, 0.5], "rotation": [-17.5, 0.0, 0.0],
        "amount": {"3": -1.0}, "windows": [{"from": -1000.0, "to": 2.0, "ease": 1.0}]}
ROD = {"name": "rod", "from": [2.0, 4.0, 6.0], "to": [3.0, 5.0, 14.0]}
SHRINK = {"type": "gauge", "motion": "stretch", "axis": "z", "origin": [0.125, 0.25, 0.375], "amount": {"3": -1.0},
          "windows": [{"from": 0.0, "to": 1000.0, "ease": 2.0}, {"from": 1.0, "to": 1000.0, "ease": 2.0}]}
STRETCH_BOXES = [
    ("side_growing", SIDE, [{"id": "s", "match": ["side"], "drivers": [GROW]}], "s", (1.5, 3, 1.0)),
    ("side_vanished", SIDE, [{"id": "s", "match": ["side"], "drivers": [GROW]}], "s", (0.5, 3, 1.0)),
    ("side_whole", SIDE, [{"id": "s", "match": ["side"], "drivers": [GROW]}], "s", (2.5, 3, 1.0)),
    ("side_other_class", SIDE, [{"id": "s", "match": ["side"], "drivers": [GROW]}], "s", (1.5, 1, 1.0)),
    ("side_on_a_turned_part", SIDE, [{"id": "t", "match": ["t_*"], "drivers": [{"type": "rotate", "axis": "y", "pivot": [0.5, 0.0, 0.5],
                                                                                    "ratio": 1.0, "input": "work"}]},
                                     {"id": "s", "match": ["side"], "ride": "t", "drivers": [GROW]}], "s", (1.25, 3, 1.0)),
    ("rod_shrinking", ROD, [{"id": "r", "match": ["rod"], "drivers": [SHRINK]}], "r", (0.75, 3, 1.0)),
    ("rod_collapsed", ROD, [{"id": "r", "match": ["rod"], "drivers": [SHRINK]}], "r", (2.5, 3, 1.0)),
    ("box_riding_a_stretched_part", ROD, [{"id": "r", "match": ["r_*"], "drivers": [SHRINK]},
                                          {"id": "b", "match": ["rod"], "ride": "r",
                                           "drivers": [{"type": "slide", "axis": "x", "amplitude": 0.25, "ratio": 1.0, "input": "work"}]}],
     "b", (2.5, 3, 1.0)),
]


def stretch_boxes():
    out = []
    for i, (bid, element, parts, pid, (w, k, p)) in enumerate(STRETCH_BOXES):
        for q in parts:
            for d in q["drivers"]:
                validate_driver(d)
        inputs = pose(i, w, k, p)
        m = part_matrix(parts, pid, inputs, WORK)
        el = flatten([element])[0]
        authored = el.corners()
        got = posed(el, m).corners()
        moved = [[v * 16 for v in apply(m, [c / 16 for c in corner])] for corner in authored]
        if any(abs(a - b) > 1e-9 for pa, pb in zip(got, moved) for a, b in zip(pa, pb)):
            raise SystemExit(f"stretch box {bid}: the posed box is not the matrix's")
        out.append({"id": bid, "element": element, "parts": parts, "part": pid, "inputs": full_inputs(inputs),
                    "authored": [[r9(v) for v in c] for c in authored], "corners": [[r9(v) for v in c] for c in got]})
    return out


def build():
    for _, d in DRIVERS:
        validate_driver(d)
    for p in RIG_PARTS:
        for d in p["drivers"]:
            validate_driver(d)
    for pid, d, _ in INVALID:
        try:
            validate_driver(d)
        except ValueError:
            continue
        raise SystemExit(f"invalid case {pid} parses")
    if progress_of({"work": WORK}) is not WORK:
        raise SystemExit("WORK does not parse")
    for pid, rig, _ in BAD_PROGRESS:
        try:
            progress_of(rig)
        except ValueError:
            continue
        raise SystemExit(f"bad progress {pid} parses")
    for pid, d, _ in WORK_INVALID:
        try:
            driver_matrix(d, pose(0, 1.0, 1, 1.0), WORK)
        except ValueError:
            continue
        raise SystemExit(f"invalid work case {pid} evaluates")
    gen = general()
    drivers = []
    for did, d in DRIVERS:
        cases = gen + (trunk_cases(d) if d["type"] in ("gauge", "roll") else [])
        drivers.append({"id": did, "driver": d,
                        "cases": [{"inputs": full_inputs(c), "matrix": mat(driver_matrix(d, c, PATH))} for c in cases]})
    poses = [{"inputs": full_inputs(c), "matrices": {p["id"]: mat(part_matrix(RIG_PARTS, p["id"], c, PATH)) for p in RIG_PARTS}}
             for c in rig_poses()]
    work_drivers = []
    for did, d in WORK_DRIVERS:
        validate_driver(d)
        cases = gen + (trunk_cases(d, WORK) if d["type"] == "gauge" else [])
        work_drivers.append({"id": did, "driver": d,
                             "cases": [{"inputs": full_inputs(c), "matrix": mat(driver_matrix(d, c, WORK))} for c in cases]})
    return {
        "_comment": "Generated by mods-src/seraphhorizons/Machines/tools/make_fixture.py from machinegen/rigmath.py, the reference "
                    "driver maths; never edit by hand. C# (DriverFixtureTests) and TypeScript (rig.test.ts) replay it. See the "
                    "script's docstring for the format.",
        "format": 2,
        "tolerance": 1e-6,
        "inputs": ["theta", "depth", "lifting", "travel", "work", "size", "presence", "feed", "oil"],
        "trunkPath": PATH,
        "drivers": drivers,
        "rig": {"parts": RIG_PARTS, "poses": poses},
        "invalid": [{"id": pid, "driver": d, "error": err} for pid, d, err in INVALID],
        "work": WORK,
        "workDrivers": work_drivers,
        "workInvalid": [{"id": pid, "driver": d, "error": err} for pid, d, err in WORK_INVALID],
        "badProgress": [{"id": pid, "rig": r, "error": err} for pid, r, err in BAD_PROGRESS],
        "stretchBoxes": stretch_boxes(),
    }


def dumps(fx):
    """One case, part or invalid driver per line, so a diff shows which changed."""
    def j(v):
        return json.dumps(v, separators=(",", ":"))
    lines = ["{"]
    for k in ("_comment", "format", "tolerance", "inputs", "trunkPath"):
        lines.append(f"\t{json.dumps(k)}: {json.dumps(fx[k], separators=(', ', ': '))},")
    lines.append('\t"drivers": [')
    blocks = []
    for d in fx["drivers"]:
        head = f'\t\t{{"id": {json.dumps(d["id"])}, "driver": {j(d["driver"])}, "cases": [\n'
        blocks.append(head + ",\n".join("\t\t\t" + j(c) for c in d["cases"]) + "\n\t\t]}")
    lines.append(",\n".join(blocks))
    lines.append("\t],")
    lines.append('\t"rig": {')
    lines.append('\t\t"parts": [')
    lines.append(",\n".join("\t\t\t" + j(p) for p in fx["rig"]["parts"]))
    lines.append("\t\t],")
    lines.append('\t\t"poses": [')
    lines.append(",\n".join("\t\t\t" + j(p) for p in fx["rig"]["poses"]))
    lines.append("\t\t]")
    lines.append("\t},")
    lines.append('\t"invalid": [')
    lines.append(",\n".join("\t\t" + j(x) for x in fx["invalid"]))
    lines.append("\t],")
    lines.append(f'\t"work": {json.dumps(fx["work"], separators=(", ", ": "))},')
    lines.append('\t"workDrivers": [')
    blocks = []
    for d in fx["workDrivers"]:
        head = f'\t\t{{"id": {json.dumps(d["id"])}, "driver": {j(d["driver"])}, "cases": [\n'
        blocks.append(head + ",\n".join("\t\t\t" + j(c) for c in d["cases"]) + "\n\t\t]}")
    lines.append(",\n".join(blocks))
    lines.append("\t],")
    for key in ("workInvalid", "badProgress", "stretchBoxes"):
        lines.append(f'\t"{key}": [')
        lines.append(",\n".join("\t\t" + j(x) for x in fx[key]))
        lines.append("\t]" + ("," if key != "stretchBoxes" else ""))
    lines.append("}")

    return "\n".join(lines) + "\n"


def main():
    ap = argparse.ArgumentParser(description="Write the rig drivers' cross-language fixture.")
    ap.add_argument("--out", type=Path, default=OUT, help=f"where to write it (default {OUT})")
    args = ap.parse_args()
    fx = build()
    text = dumps(fx)
    if json.loads(text) != json.loads(json.dumps(fx)):
        raise SystemExit("the writer lost something")
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(text)
    n = sum(len(d["cases"]) for d in fx["drivers"])
    print(f"wrote {args.out} ({len(text) // 1024} KiB): {len(fx['drivers'])} drivers, {n} cases; "
          f"rig {len(fx['rig']['parts'])} parts x {len(fx['rig']['poses'])} poses; {len(fx['invalid'])} invalid drivers")


if __name__ == "__main__":
    main()
