"""The mandrel forging station's generated model files (mods-src/seraphhorizons/MandrelStation/tools/make_shape.py).

These hold what the generator wrote to its own rules, without running it: the shipped rig parses with
the shared rig maths and uses the `requires` vocabulary of the contract, its reference poses are its own
maths, the cells are rebuilt from the shipped shape, the work is one hollow's forging or one spring's
winding, theta moves nothing, the box closes from 8 to 6 across onto the mandrel, the two sections come
off the tip, the rod is wound side by side into the coil machinegen.coil draws (the spring item's shape), fed
square to the mandrel as one wire whose far end alone grows,
and the anchors are where the contract puts them. Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import math
import re
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods-src" / "seraphhorizons"
sys.path.insert(0, str(MOD / "Machines" / "tools"))
sys.path.insert(0, str(MOD / "MandrelStation" / "tools"))

from machinegen import checks, rigmath  # noqa: E402
from machinegen.coil import Coil  # noqa: E402
from machinegen.geometry import aabb_of, flatten  # noqa: E402

_spec = importlib.util.spec_from_file_location("mandrelstation_make_shape", MOD / "MandrelStation" / "tools" / "make_shape.py")
make_shape = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(make_shape)

RIG = json.loads((MOD / "assets" / "seraphhorizons" / "config" / "mandrelstation-rig.json").read_text())
SHAPE = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "mandrelstation.json").read_text())
FRAME = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "block" / "mandrelstation_frame.json").read_text())
REFERENCE = json.loads((MOD / "tests" / "MandrelStation" / "rig-reference.json").read_text())
ITEM = json.loads((MOD / "assets" / "seraphhorizons" / "shapes" / "item" / "spring.json").read_text())
# the build order: the frame (the block), the mandrel; the work is a lead or a copper hollow, or a steel rod (a spring)
REQUIRES = {"mandrel", "hollowlead", "hollowcopper", "rodsteel", None}


def inputs(pose):
    return {k: pose[k] for k in ("theta", "work", "size", "presence")}


def matrix(pid, **ins):
    return rigmath.part_matrix(RIG["parts"], pid, ins, RIG["work"])


def posed_group(pre, rings, **ins):
    """The shipped shape's elements of metal `pre`'s rings (numbered from 1 at the shoulder), posed (voxels)."""
    out = []
    for el in flatten(SHAPE["elements"], textures={}):
        pid = rigmath.part_of(RIG["parts"], el.name)
        m = re.fullmatch(r"([lc])(\d+)[udew]+", pid)
        if m and m.group(1) == pre and int(m.group(2)) in rings:
            out.append(rigmath.posed(el, matrix(pid, **ins)))
    return out


ALL, NEAR, FAR = range(1, 9), range(1, 5), range(5, 9)


class Rig(unittest.TestCase):
    def test_parts_parse_and_use_the_vocabulary(self):
        ids = [p["id"] for p in RIG["parts"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[-1], "frame")
        for p in RIG["parts"]:
            self.assertIn(p["requires"], REQUIRES, p["id"])
            for d in p["drivers"]:
                rigmath.validate_driver(d)
        self.assertEqual({p["requires"] for p in RIG["parts"]}, REQUIRES)

    def test_every_element_has_a_part_and_every_part_an_element(self):
        names = [e["name"] for e in SHAPE["elements"]]
        self.assertEqual(len(names), len(set(names)))
        used = {rigmath.part_of(RIG["parts"], n) for n in names}
        self.assertEqual(used, {p["id"] for p in RIG["parts"]})
        frame = {e["name"] for e in FRAME["elements"]}
        self.assertEqual(frame, {n for n in names if rigmath.part_of(RIG["parts"], n) == "frame"})

    def test_the_mandrel_and_the_work_wear_their_own_texture_codes(self):
        # the renderer sets "mandrel" to the fitted rod's metal
        for req, code in (("mandrel", "#mandrel"), ("hollowlead", "#lead"), ("hollowcopper", "#copper"), ("rodsteel", "#spring")):
            pids = {p["id"] for p in RIG["parts"] if p["requires"] == req}
            els = [e for e in SHAPE["elements"] if rigmath.part_of(RIG["parts"], e["name"]) in pids]
            self.assertTrue(els, req)
            self.assertEqual({f["texture"] for e in els for f in e["faces"].values()}, {code}, req)
        self.assertIn("mandrel", SHAPE["textures"])

    def test_reference_poses_are_the_rigs_own_maths(self):
        poses = REFERENCE["poses"]
        self.assertGreater(len(poses), 40)
        self.assertEqual({p["size"] for p in poses}, {0, 1, 2, 3})
        for pose in poses[::2]:
            for pid, want in pose["matrices"].items():
                got = rigmath.part_matrix(RIG["parts"], pid, inputs(pose), RIG["work"])
                for r in range(3):
                    for c in range(4):
                        self.assertAlmostEqual(got[r][c], want[r][c], places=5, msg=f"{pid} at {inputs(pose)}")

    def test_cells_are_rebuilt_from_the_shipped_shape(self):
        cells = make_shape.shipped_cells(SHAPE, RIG["parts"], RIG["work"])
        self.assertEqual(cells, RIG["cells"])


class Forge(unittest.TestCase):
    def test_work_is_one_hollows_forging(self):
        self.assertNotIn("trunkPath", RIG)
        w = rigmath.progress_of(RIG)
        self.assertEqual((w["name"], w["unit"]), ("blows", "hollows"))
        self.assertEqual(w["end"], {"thin": 1.0, "thick": 1.0, "3": 1.0})
        forge = RIG["forge"]
        self.assertEqual(forge["hollows"], {"thin": "game:chutesection-lead", "thick": "game:chutesection-copper"})
        self.assertEqual(forge["sections"], {"thin": "seraphhorizons:pipesection-lead", "thick": "seraphhorizons:pipesection-copper"})
        self.assertEqual(forge["sectionsPerHollow"], 2)
        self.assertEqual(forge["blowsPerHollow"], {"thin": 9.0, "thick": 14.0})

    def test_theta_moves_nothing(self):
        for pid in (p["id"] for p in RIG["parts"]):
            a = matrix(pid, theta=0.0, work=0.3, size=2, presence=1.0)
            b = matrix(pid, theta=4.4, work=0.3, size=2, presence=1.0)
            self.assertEqual([[round(v, 12) for v in r] for r in a], [[round(v, 12) for v in r] for r in b], pid)

    def test_the_hollow_closes_and_stretches_evenly_together(self):
        # one motion: at every tenth of the forging the hollow is (8 - 2e) across and (8 + 8e) long
        t0, t1 = make_shape.T_FORGE
        for k, pre in ((1, "l"), (2, "c")):
            for i in range(11):
                e = i / 10
                lo, hi = aabb_of(posed_group(pre, ALL, work=t0 + (t1 - t0) * e, size=k, presence=1.0))
                self.assertAlmostEqual(hi[0] - lo[0], 8.0 - 2.0 * e, delta=0.15)
                self.assertAlmostEqual(hi[1] - lo[1], 8.0 - 2.0 * e, delta=0.15)
                self.assertAlmostEqual(hi[2] - lo[2], 8.0 + 8.0 * e, delta=0.15)
                # hung on the bar, not centred: the centre rises from HANG0 below the axis to the axis, and the
                # top wall (the bore's ceiling) bears on the bar's top throughout
                self.assertAlmostEqual((lo[1] + hi[1]) / 2, make_shape.YM - make_shape.HANG0 + make_shape.LIFT * e, delta=0.05)
                self.assertAlmostEqual(hi[1] - 1.0, make_shape.YM + make_shape.MH, delta=0.05)
                self.assertAlmostEqual(lo[2], make_shape.Z0, delta=0.05)

    def test_the_tube_stays_on_the_mandrel_at_the_end(self):
        # nothing slides off: at W 1 the 16-long tube is on the mandrel, its two sections end to end
        for k, pre in ((1, "l"), (2, "c")):
            whole = aabb_of(posed_group(pre, ALL, work=1.0, size=k, presence=1.0))
            self.assertAlmostEqual(whole[0][2], make_shape.Z0, delta=0.05)
            self.assertLessEqual(whole[1][2], make_shape.TIP + 1e-3)
            near = aabb_of(posed_group(pre, NEAR, work=1.0, size=k, presence=1.0))
            far = aabb_of(posed_group(pre, FAR, work=1.0, size=k, presence=1.0))
            for lo, hi in (near, far):
                self.assertAlmostEqual(hi[2] - lo[2], 8.0, delta=0.15)
                self.assertAlmostEqual((lo[1] + hi[1]) / 2, make_shape.YM, delta=0.05)
            self.assertAlmostEqual(far[0][2], near[1][2], delta=0.15)
        # every driver of the hollows is the forging's: no part moves beyond the closing and the stretch
        for p in RIG["parts"]:
            if p["requires"] not in ("hollowlead", "hollowcopper"):
                continue
            for d in p["drivers"]:
                if d["type"] == "gauge":
                    self.assertEqual([(w["from"], w["ease"]) for w in d["windows"]], [(0.0, 1.0)], p["id"])


SIDES = [f"sp{t}{f}" for t in range(1, 7) for f in "uwde"]       # the coil's sides, in winding order: turn, face


def posed_parts(pids, **ins):
    """The shipped shape's elements of parts `pids` (one element each), posed (voxels)."""
    out = {}
    for el in flatten(SHAPE["elements"], textures={}):
        pid = rigmath.part_of(RIG["parts"], el.name)
        if pid in pids:
            out[pid] = rigmath.posed(el, matrix(pid, **ins))
    return out


class Spring(unittest.TestCase):
    def test_one_rod_makes_one_spring_wound_as_one_chain(self):
        wind = RIG["wind"]
        self.assertEqual((wind["rod"], wind["spring"], wind["springsPerRod"], wind["class"], wind["hot"]),
                         ("game:rod-steel", "seraphhorizons:spring-steel", 1, 3, ["rodsteel"]))
        self.assertAlmostEqual(wind["turnsPerRod"], make_shape.WIND_TURNS)
        # its own class: a job of 1 for it as for each hollow's
        self.assertEqual(rigmath.work_end(RIG["work"], 3), 1.0)
        parts = [p for p in RIG["parts"] if p["requires"] == "rodsteel"]
        # the sides in winding order, then the rod's far end (each one a growth leaves) and the pieces it grows by
        self.assertEqual([p["id"] for p in parts][:24], SIDES)
        self.assertTrue(all(re.fullmatch(r"sp(t\d+|g\d+[abc])", p["id"]) for p in parts[24:]))
        # each piece on its own, moved by gauges alone: turns about fixed axes and slides, nothing stretched
        for p in parts:
            self.assertIsNone(p["ride"], p["id"])
            for d in p["drivers"]:
                self.assertEqual((d["type"], d["motion"] in ("rotate", "slide")), ("gauge", True), p["id"])

    def test_wound_it_is_the_coil_machinegen_draws(self):
        coil = Coil((make_shape.X0, make_shape.YM), make_shape.MH, make_shape.WIRE, make_shape.COIL_PITCH, make_shape.SPRING_Z0, 6)
        els = posed_parts(set(SIDES), work=1.0, size=3, presence=1.0)
        for j, pid in enumerate(SIDES):
            want = coil.side(j, "x", "x", "spring")
            self.assertLess(max(abs(els[pid].c[i] - want.c[i]) for i in range(3)), 1e-3, pid)
            self.assertLess(max(abs(els[pid].r[a][b] - want.r[a][b]) for a in range(3) for b in range(3)), 1e-5, pid)
        lo, hi = aabb_of(list(els.values()))
        # on the mandrel's body, from the shoulder to short of the nose; six turns of 2.4
        self.assertGreaterEqual(lo[2], make_shape.SHOULDER_Z[1])
        self.assertLessEqual(hi[2], make_shape.NOSE_Z[0])

    def test_at_w0_the_rod_lies_level_and_square_across_the_mandrel(self):
        # what can be seen at W 0: the first side on the top face from the top east corner, the rest of the rod running
        # on west past the mandrel; everything else waits inside the mandrel's body
        spring = [p["id"] for p in RIG["parts"] if p["requires"] == "rodsteel"]
        seen = [pid for pid in spring if make_shape.spring_seen(pid, 0.0)]
        self.assertEqual(seen, ["sp1u", "sp1w", "spt0"])
        els = posed_parts(set(spring), work=0.0, size=3, presence=1.0)
        tilt = math.sin(make_shape.COIL.tilt) + 1e-9
        for pid in seen:
            ax = [els[pid].r[i][2] for i in range(3)]
            self.assertAlmostEqual(ax[1], 0.0, places=6, msg=pid)
            self.assertLessEqual(abs(ax[2]), tilt, pid)
        lo, hi = aabb_of([els[pid] for pid in seen])
        self.assertGreater(lo[0], 0.0)
        self.assertLess(lo[0], 1.0)
        self.assertAlmostEqual(hi[1], make_shape.YM + make_shape.MH + make_shape.WIRE, delta=0.05)
        body = [el for el in flatten(SHAPE["elements"], textures={}) if el.name == "mandrel_body"][0]
        blo, bhi = body.aabb()
        for pid in set(spring) - set(seen):
            plo, phi = els[pid].aabb()
            self.assertTrue(all(blo[i] <= plo[i] and phi[i] <= bhi[i] for i in range(3)), pid)

    def test_every_feed_runs_square_to_the_mandrel(self):
        # over each feed of the plan (a side fed out of the one before it, or the far end grown), whatever moves moves
        # square to the mandrel's axis, give or take the coil's 7.4 degree lean
        spring = [p["id"] for p in RIG["parts"] if p["requires"] == "rodsteel"]
        make_shape.plan()
        feeds = [(a / make_shape.US, b / make_shape.US) for kind, a, b in make_shape.WINDOWS if kind in ("feed", "grow")]
        self.assertEqual(len(feeds), 11 + 3 * 11)
        for a, b in feeds:
            moved = 0
            for pid in spring:
                ma, mb = matrix(pid, work=a, size=3, presence=1.0), matrix(pid, work=b, size=3, presence=1.0)
                d = [(mb[i][3] - ma[i][3]) * 16 for i in range(3)]
                size = math.sqrt(sum(x * x for x in d))
                if size > 1e-3:
                    moved += 1
                    self.assertLess(abs(d[2]) / size, math.sin(math.radians(8.0)), f"{pid} over {a:.4f}..{b:.4f}")
            self.assertGreater(moved, 0)

    def test_the_rod_is_one_wire_and_only_its_far_end_grows(self):
        # every 1/200 of W, each piece that can be seen starts on another one's centre line (a side's start at the one
        # before it, the far end at the side it runs on from, a growing piece inside the far end), all one wire from
        # the first side; and the sides never change length (rigid bars, one element each, a side's run and a wire)
        spring = [p["id"] for p in RIG["parts"] if p["requires"] == "rodsteel"]
        for pid in SIDES:
            el = posed_parts({pid}, work=0.0, size=3, presence=1.0)[pid]
            self.assertAlmostEqual(el.size[2], make_shape.COIL.length + make_shape.WIRE, delta=2e-4, msg=pid)
        for i in range(201):
            W = i / 200
            els = posed_parts(set(spring), work=W, size=3, presence=1.0)
            seen = [pid for pid in spring if make_shape.spring_seen(pid, W)]
            line = {}
            for pid in seen:
                pc = make_shape.piece(pid)
                ax = [els[pid].r[q][2] for q in range(3)]
                mid = (pc.lo + pc.hi) / 2
                line[pid] = ([els[pid].c[q] - ax[q] * mid for q in range(3)], ax, pc)
            reached, todo = {"sp1u"}, ["sp1u"]
            while todo:
                cur = todo.pop()
                for pid in seen:
                    if pid in reached:
                        continue
                    for a, b in ((pid, cur), (cur, pid)):
                        start = line[a][0]
                        o, ax, pc = line[b]
                        rel = [start[q] - o[q] for q in range(3)]
                        along = sum(rel[q] * ax[q] for q in range(3))
                        off = math.sqrt(max(0.0, sum(x * x for x in rel) - along * along))
                        if off < 2e-3 and pc.lo - 2e-3 <= along <= pc.hi + 2e-3:
                            reached.add(pid)
                            todo.append(pid)
                            break
            self.assertEqual(sorted(reached), sorted(seen), f"W {W}")

    def test_the_class_is_the_job(self):
        # the spring moves for its class (3) alone; the hollows never with it
        ident = [[1.0 if r == c else 0.0 for c in range(4)] for r in range(3)]
        hollows = [p["id"] for p in RIG["parts"] if p["requires"] in ("hollowlead", "hollowcopper")]
        spring = [p["id"] for p in RIG["parts"] if p["requires"] == "rodsteel"]
        for W in (0.02, 0.31, 0.5, 0.77, 1.0):
            for k, pids in ((0, spring), (1, spring), (2, spring), (3, hollows)):
                for pid in pids:
                    got = matrix(pid, work=W, size=k, presence=1.0)
                    self.assertEqual([[round(v, 12) + 0.0 for v in r] for r in got[:3]], ident, f"{pid} with class {k}")
            self.assertTrue(any([[round(v, 12) + 0.0 for v in r] for r in matrix(pid, work=W, size=3, presence=1.0)[:3]] != ident
                                for pid in spring))
        for p in RIG["parts"]:
            if p["requires"] == "rodsteel":
                for d in p["drivers"]:
                    self.assertEqual(list(d["amount"]), ["3"], p["id"])

    def test_the_item_is_the_wound_coil(self):
        wound = posed_parts(set(SIDES), work=1.0, size=3, presence=1.0)
        item = {el.name: el for el in flatten(ITEM["elements"], textures={})}
        self.assertEqual(sorted(item), sorted(f"{pid}_wire" for pid in SIDES))
        d = None
        for pid in SIDES:
            a, b = item[f"{pid}_wire"].corners(), wound[pid].corners()
            off = [a[0][i] - b[0][i] for i in range(3)]
            d = d or off
            for p, q in zip(a, b):
                for i in range(3):
                    self.assertAlmostEqual(p[i] - q[i], d[i], delta=2e-3, msg=pid)
        lo, hi = aabb_of(list(item.values()))
        self.assertAlmostEqual(lo[1], 0.0, places=3)
        self.assertAlmostEqual(lo[0] + hi[0], 16.0, places=3)
        self.assertAlmostEqual(lo[2] + hi[2], 16.0, places=3)
        self.assertLessEqual(max(hi), 16.0)


class Anchors(unittest.TestCase):
    def test_cells_sides_and_anchors(self):
        cells = {tuple(c["pos"]) for c in RIG["cells"]}
        self.assertEqual(cells, {(0, 0, 0), (0, 0, 1)})
        # a hand station is not walked on: no lids
        self.assertFalse([c for c in RIG["cells"] if "lid" in c])
        self.assertTrue(all(c.get("boxes") for c in RIG["cells"]))
        self.assertNotIn("powerCell", RIG)
        self.assertNotIn("powerFace", RIG)
        self.assertEqual((RIG["infeedSide"], RIG["outputSide"]), ("west", "south"))
        for key in ("output", "strike"):
            self.assertEqual(len(RIG[key]["pos"]), 3)
        # the strike on the box's top over the stump's cell; the output (where gameplay drops the sections)
        # beyond the tip in the far cell
        self.assertLess(RIG["strike"]["pos"][2], 1.0)
        self.assertGreater(RIG["output"]["pos"][2], make_shape.TIP / 16)


if __name__ == "__main__":
    unittest.main()
