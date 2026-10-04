"""packtool smoke --export: reading the export the server wrote. No server needed.

Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

_spec = importlib.util.spec_from_file_location("packtool", Path(__file__).resolve().parent.parent / "packtool.py")
packtool = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(packtool)

EXAMPLE = Path(__file__).resolve().parents[2] / "schema" / "examples" / "minimal.json"


def recipe(rid, rtype):
    return {"id": rid, "type": rtype}


class ExportCounts(unittest.TestCase):
    def test_counts_per_type_from_the_example(self):
        counts, problems = packtool.export_counts(json.loads(EXAMPLE.read_text()))
        self.assertEqual(problems, [])
        self.assertEqual(counts, {"alloy": 1, "barrel": 1, "butchery": 1, "construction": 1, "examplemod:press": 1, "grid": 1, "knapping": 1})

    def test_type_with_no_recipes_is_counted_as_zero(self):
        doc = {"recipes": [recipe("grid|a|0", "grid")],
               "recipeTypes": {"grid": {"count": 1}, "vekiln": {"count": 0}}}
        counts, problems = packtool.export_counts(doc)
        self.assertEqual(problems, [])
        self.assertEqual(counts, {"grid": 1, "vekiln": 0})

    def test_count_that_disagrees_with_records(self):
        doc = {"recipes": [recipe("grid|a|0", "grid"), recipe("grid|a|1", "grid")],
               "recipeTypes": {"grid": {"count": 3}}}
        _, problems = packtool.export_counts(doc)
        self.assertEqual(problems, ["recipeTypes['grid'].count is 3, but 2 recipe(s) have it"])

    def test_record_of_unknown_type(self):
        doc = {"recipes": [recipe("grid|a|0", "grid"), recipe("x|a|0", "x")],
               "recipeTypes": {"grid": {"count": 1}}}
        _, problems = packtool.export_counts(doc)
        self.assertEqual(len(problems), 1)
        self.assertIn("'x', which is not in recipeTypes", problems[0])

    def test_duplicate_ids(self):
        doc = {"recipes": [recipe("grid|a|0", "grid"), recipe("grid|a|0", "grid")],
               "recipeTypes": {"grid": {"count": 2}}}
        _, problems = packtool.export_counts(doc)
        self.assertEqual(problems, ["duplicate recipe id grid|a|0"])

    def test_empty_export_is_a_problem(self):
        _, problems = packtool.export_counts({"recipes": [], "recipeTypes": {}})
        self.assertEqual(problems, ["no recipes exported"])

    def test_wrong_shape(self):
        _, problems = packtool.export_counts({"recipes": {}})
        self.assertEqual(problems, ["no recipes list or recipeTypes object"])


class CheckExport(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.path = Path(self.dir.name) / "recipes.json"

    def tearDown(self):
        self.dir.cleanup()

    def test_missing_file_fails_and_shows_the_exporter_log(self):
        log = ["12:00 [Server Notification] something else",
               "12:01 [Server Error] [seraphexport] export failed: Recipe registry 'vekilnrecipes' cannot be serialised: x"]
        lines, failures = packtool.check_export(self.path, log)
        self.assertEqual(lines, [])
        self.assertEqual(failures[0], f"recipe export not written to {self.path}")
        self.assertIn("Recipe registry 'vekilnrecipes' cannot be serialised", failures[1])
        self.assertEqual(len(failures), 2)

    def test_invalid_json_fails(self):
        self.path.write_text('{"recipes": [')
        _, failures = packtool.check_export(self.path, [])
        self.assertEqual(len(failures), 1)
        self.assertIn("is not valid JSON", failures[0])

    def test_valid_export_lists_counts_per_type(self):
        self.path.write_text(EXAMPLE.read_text())
        lines, failures = packtool.check_export(self.path, [])
        self.assertEqual(failures, [])
        self.assertTrue(lines[0].startswith("recipe export: 7 recipe(s) in 7 type(s), "))
        self.assertEqual(lines[1:], ["  alloy: 1", "  barrel: 1", "  butchery: 1", "  construction: 1", "  examplemod:press: 1", "  grid: 1", "  knapping: 1"])


if __name__ == "__main__":
    unittest.main()
