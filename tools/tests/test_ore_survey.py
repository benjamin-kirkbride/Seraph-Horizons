"""tools/ore-survey/ore_survey.py: grouping, ingot conversion and the targets check.

Run with `python3 -m unittest discover -s tools/tests -p test_ore_survey.py`.
"""

import argparse
import contextlib
import importlib.util
import io
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
_spec = importlib.util.spec_from_file_location("ore_survey", ROOT / "tools" / "ore-survey" / "ore_survey.py")
ore_survey = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ore_survey)


def ore_drops(code, units):
    """A dump entry like the game's: 1.25 ore chunks and 0.01 crystallised ore at twice the units."""
    path = code.split(":", 1)[1]
    return {
        "blocks": {code: [{"code": f"Item game:crystalizedore-{path[4:]}", "avg": 0.01, "last": False},
                          {"code": f"Item game:{path}", "avg": 1.25, "last": False}]},
        "items": {f"Item game:crystalizedore-{path[4:]}": {"metalUnits": 2 * units, "smeltsTo": None},
                  f"Item game:{path}": {"metalUnits": units, "smeltsTo": None}},
    }


def dump(*parts):
    out = {"blocks": {}, "items": {
        "Item game:nugget-nativecopper": {"metalUnits": None, "smeltsTo": "game:ingot-copper"},
        "Item game:nugget-hematite": {"metalUnits": None, "smeltsTo": "game:ironbloom"},
        "Item game:nugget-nativesilver": {"metalUnits": None, "smeltsTo": "game:ingot-silver"},
        "Item game:nugget-galena": {"metalUnits": None, "smeltsTo": "game:ingot-lead"},
        "Item game:nugget-rhodochrosite": {"metalUnits": None, "smeltsTo": None},
    }, "deposits": []}
    for p in parts:
        out["blocks"].update(p["blocks"])
        out["items"].update(p["items"])
    return out


COPPER = "game:ore-medium-nativecopper-granite"
DUMP = dump(ore_drops(COPPER, 20), ore_drops("game:ore-rich-hematite-granite", 30),
            ore_drops("game:ore-poor-galena_nativesilver-granite", 5))


def opts(**kw):
    base = dict(min_blocks=100, link=150, min_ingots=50, min_mineral_blocks=1000,
                gravel_link=16, min_field_blocks=100)
    return argparse.Namespace(**{**base, **kw})


class Conversion(unittest.TestCase):
    def setUp(self):
        self.c = ore_survey.Classifier(DUMP)

    def test_ore_block_is_chunks_plus_crystallised_in_nuggets(self):
        # 1.25 x 20 + 0.01 x 40 = 25.4 units = 5.08 nuggets; 100 blocks = 25.4 ingots.
        kind = self.c(COPPER)
        self.assertEqual(kind[:3], ("metal", "copper", "nativecopper"))
        self.assertAlmostEqual(kind[3], 5.08)
        self.assertAlmostEqual(100 * kind[3] / 20, 25.4)

    def test_metal_follows_the_nugget(self):
        self.assertEqual(self.c("game:ore-rich-hematite-granite")[1], "iron")
        # Argentiferous galena is a lead ore (#690).
        self.assertEqual(self.c("game:ore-poor-galena_nativesilver-granite")[1], "lead")

    def test_minerals_gravel_and_ignored(self):
        self.assertEqual(self.c("interestingoregen:saltpeterore")[:2], ("mineral", "saltpeter"))
        self.assertEqual(self.c("game:ore-bituminouscoal-shale")[:2], ("mineral", "coal"))
        # A nugget that smelts to nothing is a mineral, valued in blocks.
        self.assertEqual(self.c("game:ore-poor-rhodochrosite-chert")[:2], ("mineral", "manganese"))
        self.assertEqual(self.c("game:richgravel-granite")[:2], ("gravel", "richgravel"))
        self.assertIsNone(self.c("game:looseores-nativecopper-granite"))
        self.assertIsNone(self.c("game:ore-emerald-peridotite"))
        self.assertIsNone(self.c("game:ore-quartz-granite"))


class Grouping(unittest.TestCase):
    def test_flood_fill_joins_all_26_neighbours(self):
        cells = {(0, 0, 0): [10, 1.0, 20], (1, 1, 1): [5, 0.5, 3], (2, 2, 0): [1, 0.1, 9],
                 (5, 0, 0): [7, 0.7, 40]}
        frags = sorted(ore_survey.fragments(cells), key=lambda f: -f["blocks"])
        self.assertEqual([f["blocks"] for f in frags], [16, 7])
        self.assertEqual(frags[0]["minDepth"], 3)
        self.assertAlmostEqual(frags[0]["nuggets"], 1.6)
        self.assertAlmostEqual(frags[0]["x"], 8 * 1 + 4)

    def test_merge_is_single_linkage_under_the_link(self):
        f = lambda x, b, d=50: {"x": x, "z": 0, "blocks": b, "nuggets": b, "minDepth": d, "ores": ["a"]}
        # 0-140-280 chain within 150 m; 500 stands alone.
        deps = sorted(ore_survey.merge([f(0, 100), f(140, 300, 2), f(280, 100), f(500, 100)], 150),
                      key=lambda d: d["x"])
        self.assertEqual([(d["blocks"], d["fragments"]) for d in deps], [(500, 3), (100, 1)])
        self.assertEqual(deps[0]["x"], 140)  # the largest fragment's centre
        self.assertEqual(deps[0]["minDepth"], 2)
        self.assertEqual(len(ore_survey.merge([f(0, 1), f(150, 1)], 150)), 2)

    def test_gravel_fields_join_within_16_m_horizontally(self):
        cells = {(0, 0, 10): [50, 0, 1], (0, 0, 2): [5, 0, 30],   # same column, any height
                 (2, 0, 10): [50, 0, 2],                         # 16 m away: joined
                 (5, 0, 10): [9, 0, 0],                          # 24 m from (2, 0): not
                 (7, 2, 10): [9, 0, 0]}                          # 22.6 m diagonal from (5, 0): not
        fl = sorted(ore_survey.fields(cells, 16), key=lambda f: -f["blocks"])
        self.assertEqual([f["blocks"] for f in fl], [105, 9, 9])
        self.assertEqual(fl[0]["minDepth"], 1)
        self.assertAlmostEqual(fl[0]["x"], (4 * 55 + 20 * 50) / 105)

    def test_nearest_stays_within_a_seed(self):
        pts = [{"seed": 1, "x": 0, "z": 0}, {"seed": 1, "x": 300, "z": 400}, {"seed": 2, "x": 1, "z": 0}]
        self.assertEqual(ore_survey.nearest(pts), [500, 500])

    def test_quantile_matches_the_scratch_survey(self):
        self.assertEqual(ore_survey.quantile([5, 1, 4, 2, 3], .5), 3)
        self.assertEqual(ore_survey.quantile([5, 1, 4, 2, 3], .9), 5)
        self.assertEqual(ore_survey.quantile([], .5), 0)


class Summary(unittest.TestCase):
    def write_run(self, tmp, seed, rows):
        run = Path(tmp) / f"pack-s{seed}"
        run.mkdir()
        (run / "orescan.json").write_text(json.dumps({"seed": seed, "areaKm2": 1.0, "blocks": {}}))
        (run / "orescan.json.cells.csv").write_text("".join(",".join(map(str, r)) + "\n" for r in rows))
        return run

    def test_end_to_end(self):
        with tempfile.TemporaryDirectory() as tmp:
            (Path(tmp) / "pack-dump").mkdir()
            (Path(tmp) / "pack-dump" / "orescan.json").write_text(json.dumps(DUMP))
            rows = (
                # a 400-block copper deposit in two fragments 100 m apart, one at the surface
                [(COPPER, x, 0, 10, 100, 40) for x in range(2)] + [(COPPER, 12 + x, 0, 10, 100, 4) for x in range(2)]
                # a 20-block pocket far away
                + [(COPPER, 200, 200, 10, 20, 1)]
                # rich gravel: a 300-block field and a stray 10
                + [("game:richgravel-granite", 50 + x, 50, 13, 100, 0) for x in range(3)]
                + [("game:richgravel-granite", 100, 100, 13, 10, 0)]
                + [("game:ore-emerald-granite", 1, 1, 1, 1, 50)]
            )
            run = self.write_run(tmp, 7, rows)
            out = Path(tmp) / "s.json"
            with contextlib.redirect_stdout(io.StringIO()) as text:
                ore_survey.main(["summary", str(run), "--json", str(out), "--min-ingots", "10"])
            s = json.loads(out.read_text())
            cu = s["metals"]["copper"]
            self.assertEqual(cu["deposits"], 1)
            self.assertAlmostEqual(cu["ingotsMedian"], 400 * 5.08 / 20)
            self.assertEqual(cu["surfaceShare"], 1)
            self.assertAlmostEqual(cu["pocketsPerKm2"], 1)
            self.assertAlmostEqual(cu["pocketShare"], 20 / 420)
            self.assertAlmostEqual(cu["ingotsPerKm2"], 420 * 5.08 / 20)
            g = s["gravel"]
            self.assertEqual((g["fields"], g["blocksMedian"], g["scatteredBlocksPerKm2"]), (1, 300, 10))
            self.assertIn("copper", text.getvalue())

            # A copy at half the size compares at 0.50x.
            half = json.loads(out.read_text())
            half["metals"]["copper"]["ingotsMedian"] /= 2
            (Path(tmp) / "h.json").write_text(json.dumps(half))
            with contextlib.redirect_stdout(io.StringIO()) as text:
                ore_survey.main(["compare", str(out), str(Path(tmp) / "h.json")])
            self.assertIn("0.50x", text.getvalue())


class Targets(unittest.TestCase):
    TARGETS = {"tolerance": 1.5, "areaPerDeposit_km2": 25,
               "metals": {"copper": {"small": 150, "typical": 400, "large": 1000},
                          "gold": {"small": 30, "typical": 80, "large": 200}},
               "gravel": {"areaPerField_km2": 2.25, "blocks": [300, 600]}}

    def summary(self, **copper):
        cu = {"deposits": 5, "depositsPerKm2": 1 / 25, "ingotsP10": 150, "ingotsMedian": 400, "ingotsP90": 1000}
        return {"metals": {"copper": {**cu, **copper}},
                "gravel": {"fieldsPerKm2": 1 / 2.25, "blocksMedian": 450}}

    def results(self, s):
        return {what: ok for what, _, _, ok in ore_survey.check(s, self.TARGETS)}

    def test_on_target_passes_and_missing_metal_fails(self):
        r = self.results(self.summary())
        self.assertFalse(r.pop("gold deposits"))
        self.assertTrue(all(r.values()), r)

    def test_tolerance_is_a_factor_both_ways(self):
        r = self.results(self.summary(ingotsMedian=590, ingotsP10=99, depositsPerKm2=1 / 40))
        self.assertTrue(r["copper ingots median"])
        self.assertFalse(r["copper ingots p10"])
        self.assertFalse(r["copper deposits per 25 km²"])
        s = self.summary()
        s["gravel"]["blocksMedian"] = 700
        self.assertFalse(self.results(s)["gravel blocks per field, median"])

    def test_shipped_targets_are_the_epics(self):
        t = ore_survey.load_targets(ROOT / "tools" / "ore-survey" / "targets.json")
        self.assertEqual(t["metals"]["copper"], {"small": 150, "typical": 400, "large": 1000})
        self.assertEqual(t["metals"]["gold"], {"small": 30, "typical": 80, "large": 200})
        self.assertEqual(len(t["metals"]), 12)
        self.assertEqual(t["areaPerDeposit_km2"], 25)
        self.assertEqual(t["gravel"], {"areaPerField_km2": 2.25, "blocks": [300, 600]})


if __name__ == "__main__":
    unittest.main()
