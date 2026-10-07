"""machinegen: the machines' shared generator package (mods-src/seraphhorizons/Machines/tools/).

The driver maths is the reference the C# renderer and the site's viewer are held to, through
tests/Machines/driver-fixture.json and the mill's tests/BuckingSawmill/rig-reference.json; these
tests pin the maths itself, keep both files in step with it, and cover the shared geometry, checks
and writers. They need no game and no fetched mods.

Run with `python3 -m unittest discover -s tools/tests`.
"""

import copy
import importlib.util
import json
import math
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods-src" / "seraphhorizons"
TOOLS = MOD / "Machines" / "tools"
sys.path.insert(0, str(TOOLS))

from machinegen import checks, geometry, output, rigmath  # noqa: E402
from machinegen.geometry import El  # noqa: E402

_spec = importlib.util.spec_from_file_location("make_fixture", TOOLS / "make_fixture.py")
make_fixture = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_fixture)

PATH = {"nose0": 0.5, "lengths": {"thin": 4, "thick": 5}, "tailStop": 9.0}


def box(name, lo, hi, part="frame", faces=("north", "east", "south", "west", "up", "down")):
    size = [hi[k] - lo[k] for k in range(3)]
    c = [(lo[k] + hi[k]) / 2 for k in range(3)]
    return El(name, size, c, [row[:] for row in geometry.IDENT], {d: {"texture": "#oak", "uv": [0.0, 0.0, 4.0, 4.0]} for d in faces}, part)


def close(test, a, b, tol=1e-12):
    for ra, rb in zip(a, b):
        for x, y in zip(ra, rb):
            test.assertAlmostEqual(x, y, delta=tol)


def gauge(**kw):
    d = {"type": "gauge", "motion": "slide", "axis": "y", "amount": {"thin": 1.0, "thick": 2.0}}
    d.update(kw)
    return d


def offset(d, **inputs):
    return rigmath.driver_matrix(d, inputs, PATH)[1][3]


class FixtureFile(unittest.TestCase):
    def test_the_fixture_is_what_the_maths_writes(self):
        on_disk = (MOD / "tests" / "Machines" / "driver-fixture.json").read_text()
        self.assertEqual(on_disk, make_fixture.dumps(make_fixture.build()),
                         "driver-fixture.json is stale or hand-edited: run Machines/tools/make_fixture.py")

    def test_the_fixture_covers_every_driver_type_and_input(self):
        fx = json.loads((MOD / "tests" / "Machines" / "driver-fixture.json").read_text())
        types = {d["driver"]["type"] for d in fx["drivers"]}
        self.assertEqual(types, set(rigmath.DRIVER_TYPES))
        inputs = {d["driver"].get("input") for d in fx["drivers"]}
        self.assertTrue({"theta", "travel", "feed", "trunk"} <= inputs)
        gates = {d["driver"].get("lifting") for d in fx["drivers"] if d["driver"]["type"] == "step"}
        self.assertEqual(gates, {None, "hold", "block", "trip"})
        cases = [c["inputs"] for d in fx["drivers"] for c in d["cases"]]
        self.assertEqual({c["size"] for c in cases}, {0, 1, 2})
        self.assertTrue({0.0, 0.3, 1.0} <= {c["presence"] for c in cases})
        for c in cases:
            self.assertEqual(list(c), fx["inputs"])
        self.assertGreaterEqual(len(fx["rig"]["poses"]), 40)


class Parsing(unittest.TestCase):
    def test_every_invalid_driver_is_rejected(self):
        for pid, d, _ in make_fixture.INVALID:
            with self.subTest(pid), self.assertRaises(ValueError):
                rigmath.validate_driver(d)

    def test_every_fixture_driver_parses(self):
        for pid, d in make_fixture.DRIVERS:
            with self.subTest(pid):
                rigmath.validate_driver(d)

    def test_the_mills_shipped_rig_parses(self):
        rig = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "buckingmill-rig.json").read_text())
        for p in rig["parts"]:
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual(checks.lid_gaps(rig["cells"]), [])

    def test_input_and_rectified_conflict(self):
        d = {"type": "rotate", "axis": "x", "pivot": [0, 0, 0], "rectified": True, "input": "travel"}
        with self.assertRaises(ValueError):
            rigmath.driver_matrix(d, {"theta": 1.0})


class Inputs(unittest.TestCase):
    def test_travel_defaults_to_the_angle_either_way(self):
        self.assertEqual(rigmath.full_inputs({"theta": -2.5})["travel"], 2.5)
        self.assertEqual(rigmath.full_inputs({"theta": -2.5, "travel": 7.0})["travel"], 7.0)

    def test_rectified_is_input_travel(self):
        a = {"type": "rotate", "axis": "y", "pivot": [0.3, 1.0, 0.2], "ratio": -1.3, "rectified": True}
        b = {"type": "rotate", "axis": "y", "pivot": [0.3, 1.0, 0.2], "ratio": -1.3, "input": "travel"}
        for theta, travel in ((-1.0, 4.0), (2.0, 2.0)):
            inputs = {"theta": theta, "travel": travel}
            self.assertEqual(rigmath.driver_matrix(a, inputs), rigmath.driver_matrix(b, inputs))

    def test_each_input_drives_its_driver(self):
        for name in ("theta", "travel", "feed", "trunk"):
            d = {"type": "slide", "axis": "x", "amplitude": 1.0, "input": name}
            inputs = {"theta": 0.0, "travel": 0.0, "feed": 0.0, "trunk": 0.0, name: math.pi / 2}
            self.assertAlmostEqual(rigmath.driver_matrix(d, inputs)[0][3], 1.0, delta=1e-12, msg=name)

    def test_the_trunk_path(self):
        self.assertEqual(rigmath.nose(PATH, 2.0), 2.5)
        self.assertEqual(rigmath.tail(PATH, 2.0, 2), -2.5)
        self.assertEqual(rigmath.tail(PATH, 2.0, 0), 2.5)
        self.assertEqual(rigmath.trunk_end(PATH, 1), 12.5)
        self.assertEqual(rigmath.trunk_end(PATH, 2), 13.5)


class Gauge(unittest.TestCase):
    W = {"from": 2.0, "to": 3.0, "ease": 0.5}

    def test_no_trunk_no_motion(self):
        d = gauge(windows=[self.W])
        self.assertEqual(offset(d, trunk=2.0, size=0, presence=1.0), 0.0)
        self.assertEqual(offset(gauge(mode="present"), size=0, presence=1.0), 0.0)

    def test_present_follows_presence(self):
        d = gauge(mode="present")
        self.assertAlmostEqual(offset(d, size=1, presence=0.3), 0.3)
        self.assertAlmostEqual(offset(d, size=2, presence=0.3), 0.6)

    def test_occupancy_eases_in_with_the_nose_and_out_with_the_tail(self):
        d = gauge(windows=[self.W])
        # nose = 0.5 + T: arrives at from (T 1.5), full by from + ease (T 2.0)
        self.assertEqual(offset(d, trunk=1.5, size=1, presence=1.0), 0.0)
        self.assertAlmostEqual(offset(d, trunk=1.75, size=1, presence=1.0), 0.5)
        self.assertEqual(offset(d, trunk=2.0, size=1, presence=1.0), 1.0)
        # tail = nose - 4: full until to - ease (T 6.0), gone at to (T 6.5)
        self.assertEqual(offset(d, trunk=6.0, size=1, presence=1.0), 1.0)
        self.assertAlmostEqual(offset(d, trunk=6.25, size=1, presence=1.0), 0.5)
        self.assertEqual(offset(d, trunk=6.5, size=1, presence=1.0), 0.0)
        # a thick trunk is a block longer, so it leaves a block later
        self.assertEqual(offset(d, trunk=6.5, size=2, presence=1.0), 2.0)

    def test_presence_scales_occupancy(self):
        d = gauge(windows=[self.W])
        self.assertAlmostEqual(offset(d, trunk=3.0, size=1, presence=0.3), 0.3)
        self.assertEqual(offset(d, trunk=3.0, size=1, presence=0.0), 0.0)

    def test_gain_finishes_early_per_class(self):
        d = gauge(windows=[{**self.W, "gain": {"thick": 6.0}}])
        # a twelfth of the ease in: thin is at 1/12, thick (gain 6) at a half
        t = 1.5 + 0.5 / 12
        self.assertAlmostEqual(offset(d, trunk=t, size=1, presence=1.0), 1.0 / 12)
        self.assertAlmostEqual(offset(d, trunk=t, size=2, presence=1.0), 2.0 * 0.5)
        self.assertAlmostEqual(offset(d, trunk=1.5 + 0.5 / 6, size=2, presence=1.0), 2.0)
        self.assertEqual(offset(d, trunk=2.0, size=2, presence=1.0), 2.0)                # capped at 1

    def test_windows_combine_by_max_not_sum(self):
        a = {"from": 2.0, "to": 3.0, "ease": 0.5}
        b = {"from": 2.0, "to": 3.0, "ease": 1.0}
        d = gauge(windows=[a, b])
        self.assertAlmostEqual(offset(d, trunk=1.75, size=1, presence=1.0), 0.5)   # max(0.5, 0.25)
        self.assertEqual(offset(d, trunk=3.0, size=1, presence=1.0), 1.0)          # not 2

    def test_lobes_follow_psi_scaled_by_occupancy(self):
        d = {"type": "gauge", "motion": "rotate", "axis": "x", "pivot": [0.0, 0.0, 0.0], "amount": {"thin": 0.0, "thick": 0.5},
             "windows": [self.W], "lobes": {"ratio": 4.0, "phase": 0.25, "amplitude": {"thin": 0.0, "thick": 0.1}}}
        inputs = rigmath.full_inputs({"trunk": 3.0, "size": 2, "presence": 0.5, "travel": 1.2})
        self.assertAlmostEqual(rigmath.gauge_amount(d, inputs, PATH), 0.5 * 0.5 + 0.5 * 0.1 * math.cos(4.0 * 1.2 + 0.25))
        inputs["work"] = 0.0
        self.assertEqual(rigmath.gauge_amount(d, inputs, PATH), 0.0)

    def test_rotate_turns_about_its_pivot(self):
        d = gauge(motion="rotate", axis="z", pivot=[1.0, 2.0, 0.0], amount={"thin": math.pi / 2, "thick": 0.0}, mode="present")
        m = rigmath.driver_matrix(d, {"size": 1, "presence": 1.0}, PATH)
        p = rigmath.apply(m, [2.0, 2.0, 0.0])
        close(self, [p], [[1.0, 3.0, 0.0]])


class Roll(unittest.TestCase):
    D = {"type": "roll", "axis": "z", "pivot": [0.0, 0.0, 0.0], "at": 4.0, "ratio": 2.0}

    def angle(self, **inputs):
        return rigmath.roll_angle(self.D, rigmath.full_inputs(inputs), PATH)

    def test_turns_only_while_the_trunk_is_over_it(self):
        self.assertEqual(self.angle(trunk=3.0, size=1), 0.0)           # nose 3.5, short of 4
        self.assertEqual(self.angle(trunk=4.5, size=1), 2.0)           # nose 5: one block over it
        self.assertEqual(self.angle(trunk=7.5, size=1), 8.0)           # tail leaves: L 4
        self.assertEqual(self.angle(trunk=12.0, size=1), 8.0)          # stays where it stopped
        self.assertEqual(self.angle(trunk=12.0, size=2), 10.0)         # thick: L 5
        self.assertEqual(self.angle(trunk=12.0, size=0), 0.0)

    def test_ignores_presence(self):
        self.assertEqual(self.angle(trunk=4.5, size=1, presence=0.0), self.angle(trunk=4.5, size=1, presence=1.0))


class MillMaths(unittest.TestCase):
    def test_the_wrappers_are_the_generic_maths(self):
        for _, d in make_fixture.DRIVERS[:11]:
            for theta, depth, lifting, travel in ((0.7, 0.5, 0.5, 0.7), (-2.3, 0.003, 0.6, 2.3), (4.6, 0.96, 1.0, None)):
                self.assertEqual(rigmath.driver_matrix_mill(d, theta, depth, lifting, travel),
                                 rigmath.driver_matrix(d, {"theta": theta, "depth": depth, "lifting": lifting, "travel": travel}))

    def test_the_mills_reference_poses_are_its_rig_through_this_maths(self):
        rig = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "buckingmill-rig.json").read_text())
        ref = json.loads((MOD / "tests" / "BuckingSawmill" / "rig-reference.json").read_text())
        for pose in ref["poses"][::7]:
            for pid, want in pose["matrices"].items():
                got = output.round_matrix(rigmath.part_matrix_mill(rig["parts"], pid, pose["theta"], pose["depth"], pose["lifting"], pose["travel"]))
                self.assertEqual(got, want, f"{pid} at {pose['theta']}, {pose['depth']}")

    def test_part_of_takes_the_first_match(self):
        parts = [{"id": "ring", "match": ["ring_*"]}, {"id": "tyre", "match": ["ringtyre_*"]}, {"id": "frame", "match": ["*"]}]
        self.assertEqual(rigmath.part_of(parts, "ring_body"), "ring")
        self.assertEqual(rigmath.part_of(parts, "ringtyre_1"), "tyre")
        self.assertEqual(rigmath.part_of(parts, "post"), "frame")


class Work(unittest.TestCase):
    """The generic progress, a rig's `work`: a named quantity, a point on its own scale; a trunkPath is
    the trunk-flavoured case, and "trunk" the trunk-flavoured spelling of the work input."""

    WORK = {"name": "teeth cut", "unit": "teeth", "step": 0.005, "end": {"thin": 12.0, "thick": 20.0}}

    def test_progress_of_reads_work_or_a_trunk_path_not_both(self):
        self.assertIs(rigmath.progress_of({"work": self.WORK}), self.WORK)
        self.assertIs(rigmath.progress_of({"trunkPath": PATH}), PATH)
        self.assertIsNone(rigmath.progress_of({}))
        for bad in ({"work": self.WORK, "trunkPath": PATH}, {"work": {"end": {"thin": 1.0, "thick": 1.0}}},
                    {"work": {"unit": "t", "end": {"thin": 1.0}}}, {"work": {"unit": "t", "step": 0, "end": {"thin": 1.0, "thick": 1.0}}},
                    {"work": dict(self.WORK, lengths={"thin": 1.0, "thick": 1.0})}):
            with self.assertRaises(ValueError):
                rigmath.progress_of(bad)

    def test_a_work_quantity_is_a_point_with_an_end_per_class(self):
        self.assertEqual([rigmath.work_end(self.WORK, k) for k in (0, 1, 2)], [0.0, 12.0, 20.0])
        self.assertEqual(rigmath.trunk_length(self.WORK, 2), 0.0)
        d = {"type": "gauge", "motion": "slide", "axis": "y", "amount": {"thin": 1.0, "thick": 1.0},
             "windows": [{"from": 3.1, "to": 3.68, "ease": 0.28}]}
        frac = lambda w: rigmath.gauge_fraction(d, rigmath.full_inputs({"work": w, "size": 1, "presence": 1.0}), self.WORK)  # noqa: E731
        self.assertEqual(frac(3.1), 0.0)
        self.assertAlmostEqual(frac(3.24), 0.5)
        self.assertAlmostEqual(frac(3.38), 1.0)
        self.assertAlmostEqual(frac(3.54), 0.5)
        self.assertEqual(frac(3.68), 0.0)

    def test_trunk_is_the_same_input_as_work(self):
        d = {"type": "rotate", "axis": "x", "pivot": [0.0, 0.0, 0.0], "ratio": 2.0, "input": "trunk"}
        e = dict(d, input="work")
        self.assertEqual(rigmath.driver_matrix(d, {"trunk": 0.7}), rigmath.driver_matrix(e, {"work": 0.7}))
        self.assertEqual(rigmath.full_inputs({"trunk": 0.7})["work"], 0.7)

    def test_a_roll_needs_a_trunk_path(self):
        d = {"type": "roll", "axis": "z", "pivot": [0.0, 0.0, 0.0], "at": 1.0, "ratio": 1.0}
        with self.assertRaises(ValueError):
            rigmath.driver_matrix(d, {"work": 2.0, "size": 1, "presence": 1.0}, self.WORK)


class Geometry(unittest.TestCase):
    def test_euler_round_trip(self):
        r = geometry.from_euler(20.0, -35.0, 110.0)
        close(self, geometry.from_euler(*geometry.euler_xyz(r)), r, 1e-9)

    def test_beam_splits_into_block_lengths(self):
        t = box("t", [0, 0, 0], [1, 1, 1])
        segs = geometry.beam(t, [0, 0, 0], [40, 2, 3], "b", "frame")
        self.assertEqual([s.name for s in segs], ["b_1", "b_2", "b_3"])
        self.assertAlmostEqual(sum(s.size[0] for s in segs), 40.0)

    def test_octagon_is_four_strips_about_x(self):
        t = box("t", [0, 0, 0], [1, 1, 1])
        oct_ = geometry.octagon(t, 0.0, 1.0, 8.0, 8.0, 3.0, "o", "drum")
        self.assertEqual(len(oct_), 4)
        for el in oct_:
            lo, hi = el.aabb()
            self.assertLessEqual(hi[1], 8.0 + 3.0 / math.cos(math.pi / 8) + 1e-9)

    def test_strut_runs_between_its_points(self):
        t = box("t", [0, 0, 0], [1, 1, 1])
        el = geometry.strut(t, [0.0, 0.0, 0.0], [0.0, 3.0, 4.0], 1.0, 1.0, "s", "rod")
        self.assertAlmostEqual(abs(el.size[1]), 5.0)
        close(self, [el.c], [[0.0, 1.5, 2.0]])


class Checks(unittest.TestCase):
    def test_coplanar_faces_are_found_and_fixed(self):
        a = box("a", [0, 0, 0], [4, 4, 4])
        b = box("b", [4, 0, 0], [6, 2, 2], part="other")            # pressed on a's east face, another part: kept
        c = box("c", [0, 4, 0], [2, 6, 2], part="other")
        d = box("d", [1, 4, 0], [3, 5, 2], part="other")            # c and d: north faces both at z 0, overlapping
        e = box("e", [0, 0, 4], [4, 4, 6])                          # pressed on a's south face, same part: hidden
        els = [a, b, c, d, e]
        self.assertTrue(checks.coplanar_faces(els))
        _, hidden = checks.fix_coplanar(els, lambda es, _pose: [rigmath.posed(x, rigmath.m4()) for x in es], [None])
        self.assertEqual(hidden, 2)                                  # a's south and e's north
        self.assertEqual(checks.coplanar_faces(els), [])
        self.assertLess(abs(d.size[2]), 2.0)                         # d, the smaller, was moved in

    def test_obb_tests(self):
        a = box("a", [0, 0, 0], [2, 2, 2])
        self.assertTrue(checks.obb_overlap(a, [1, 1, 1], [3, 3, 3]))
        self.assertFalse(checks.obb_overlap(a, [2, 0, 0], [3, 2, 2]))    # touching faces only
        b = box("b", [1.5, 0, 0], [3, 2, 2])
        geometry.rotate([b], "z", 45.0, b.c)
        self.assertTrue(checks.obb_obb(a, b))
        self.assertEqual(checks.touching([a], [b]), {("a", "b")})

    def test_cells(self):
        self.assertEqual(checks.cells_touched([0.0, 0.0, 0.0], [16.0, 17.0, 16.0]), [(0, 0, 0), (0, 1, 0)])
        self.assertEqual(checks.box_overhang([-1.0, 0.0, 0.0], [16.0, 16.0, 16.0], (1, 1, 1)), 1.0)
        els = [box("a", [0, 0, 0], [16, 2, 16]), box("b", [0, 14, 0], [16, 16, 16])]
        self.assertEqual(checks.cell_boxes(els, (0, 0, 0)), [[0.0, 0.0, 0.0, 1.0, 0.125, 1.0], [0.0, 0.875, 0.0, 1.0, 1.0, 1.0]])
        self.assertIsNone(checks.cell_boxes(els, (1, 0, 0)))

    def test_lids_go_on_each_columns_top_cell_as_one_deck_per_layer(self):
        cells = [{"pos": [0, 0, 0], "boxes": [[0, 0, 0, 1, 0.5, 1]]},
                 {"pos": [0, 1, 0], "boxes": [[0, 0, 0, 0.25, 0.75, 1]]},
                 {"pos": [1, 1, 0], "boxes": [[0, 0, 0, 1, 0.5, 1]]},      # same layer: raised to 0.75
                 {"pos": [2, 0, 0], "hollow": True},                        # a hollow cell's top is 1,
                 {"pos": [3, 0, 0], "boxes": [[0, 0, 0, 1, 0.25, 1]], "lid": 0.5}]   # and an old lid is replaced
        got = checks.with_lids(cells)
        self.assertEqual([c.get("lid") for c in got], [None, 0.75, 0.75, 1.0, 1.0])
        self.assertTrue(got[3]["hollow"])
        self.assertEqual(got[1]["boxes"], cells[1]["boxes"])
        self.assertNotIn("lid", cells[1])                                   # (the input is left alone)
        self.assertEqual(checks.lid_gaps(got), [])
        self.assertEqual(checks.lid_gaps(cells[:3]), [(0, 0), (1, 0)])

    def test_lids_go_only_on_the_columns_picked(self):
        cells = [{"pos": [0, 0, 0], "hollow": True},
                 {"pos": [0, 1, 0], "boxes": [[0, 0, 0, 1, 0.5, 1]]},
                 {"pos": [1, 0, 0], "boxes": [[0, 0, 0, 1, 0.75, 1]]},      # left out: its height does not raise the deck
                 {"pos": [2, 0, 0], "boxes": [[0, 0, 0, 1, 0.25, 1]], "lid": 0.5}]   # left out: an old lid is dropped
        picked = lambda x, z: x == 0                                          # noqa: E731
        got = checks.with_lids(cells, picked)
        self.assertEqual([c.get("lid") for c in got], [None, 0.5, None, None])
        self.assertEqual(checks.lid_gaps(got, picked), [])
        self.assertEqual(checks.lid_gaps(got), [(1, 0), (2, 0)])
        self.assertEqual(checks.lid_gaps(cells, picked), [(0, 0), (2, 0)])   # no lid where wanted, and one where not

    def test_frame_floating(self):
        frame = [box("post", [0, 0, 0], [2, 10, 2]), box("beam", [0, 10, 0], [10, 12, 2]), box("loose", [20, 20, 20], [21, 21, 21])]
        seen, floating = checks.frame_floating(frame)
        self.assertEqual(seen, {0, 1})
        self.assertEqual(floating, ["loose"])

    def test_supports_and_bearings(self):
        frame = [box("bearing_w", [0, 4, 4], [2, 8, 8]), box("bearing_e", [10, 4, 4], [12, 8, 8]), box("post", [20, 0, 0], [22, 9, 9])]
        shaft = [box("shaft", [0, 5.5, 5.5], [12, 6.5, 6.5], part="shaft")]
        found, span = checks.supports(frame, shaft, 0, (6.0, 6.0))
        self.assertEqual(found, ["bearing_w", "bearing_e"])
        self.assertEqual(span, (0.0, 12.0))
        self.assertAlmostEqual(checks.bearing_margin(frame[0], shaft, 0), 1.5)
        self.assertEqual(checks.bearing_margin(frame[2], shaft, 0), 1e9)


class Output(unittest.TestCase):
    def test_writers_are_compact_and_parse(self):
        els = [box("a", [0, 0, 0], [1, 2, 3]), box("b", [1, 0, 0], [2, 1, 1])]
        geometry.rotate([els[1]], "y", 30.0, els[1].c)
        shape = output.shape_json(els, "test", {"oak": "game:block/wood/debarked/oak", "metal": "x"})
        self.assertEqual(shape["textures"], {"oak": "game:block/wood/debarked/oak"})
        text = output.shape_dumps(shape)
        self.assertEqual(json.loads(text), json.loads(json.dumps(shape)))
        self.assertEqual(len(text.splitlines()), 11)       # 5 head keys, braces, 2 elements
        self.assertNotIn("rotationY", shape["elements"][0])
        self.assertAlmostEqual(shape["elements"][1]["rotationY"], 30.0)
        rig = {"_comment": "c", "cells": [{"pos": [0, 0, 0]}], "parts": [{"id": "frame"}], "powerFace": "west"}
        self.assertEqual(json.loads(output.rig_dumps(rig)), rig)
        ref = {"_comment": "c", "poses": [{"theta": 0.0}, {"theta": 1.0}]}
        self.assertEqual(json.loads(output.reference_dumps(ref)), ref)

    def test_origin_shift(self):
        parts = [{"id": "p", "drivers": [{"type": "rotate", "pivot": [1.0, 2.0, 3.0]}, {"type": "stretch", "anchor": [0.5, 0.5, 0.5]}]}]
        before = copy.deepcopy(parts)
        moved = output.shift_parts(parts, [-5.0, 0.0, -1.0])
        self.assertEqual(parts, before)
        self.assertEqual(moved[0]["drivers"][0]["pivot"], [-4.0, 2.0, 2.0])
        self.assertEqual(moved[0]["drivers"][1]["anchor"], [-4.5, 0.5, -0.5])
        self.assertEqual(output.shift_cell([5, 0, 1], (5, 0, 1)), [0, 0, 0])


if __name__ == "__main__":
    unittest.main()
