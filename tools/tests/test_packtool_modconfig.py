"""packtool's ModConfig overrides: what pack/config/ModConfig turns into in the Cairn manifest.

Run with `python3 -m unittest discover -s tools/tests`.
"""

import contextlib
import importlib.util
import io
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
_spec = importlib.util.spec_from_file_location("packtool", ROOT / "tools" / "packtool.py")
packtool = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(packtool)


class CollectModConfig(unittest.TestCase):
    def collect(self, files: dict) -> dict:
        with tempfile.TemporaryDirectory() as tmp:
            for name, text in files.items():
                path = Path(tmp) / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(text)
            return packtool.collect_mod_config(Path(tmp))

    def refused(self, text: str) -> str:
        err = io.StringIO()
        with contextlib.redirect_stderr(err), self.assertRaises(SystemExit):
            self.collect({"x.yaml": text})
        return err.getvalue()

    def test_json_and_yaml_are_keyed_by_relative_path(self):
        got = self.collect({
            "a.json": '{"list": ["b"]}',
            "sub/c.json": '{"n": 1}',
            "mod.yaml": "# why\n\nmin_distance: 1200\nchance: 1.5\non: true\nname: \"x y\"\n",
            "notes.txt": "ignored",
        })
        self.assertEqual(got, {
            "a.json": {"list": ["b"]},
            "mod.yaml": {"min_distance": 1200, "chance": 1.5, "on": True, "name": "x y"},
            "sub/c.json": {"n": 1},
        })
        self.assertIsInstance(got["mod.yaml"]["min_distance"], int)

    def test_yaml_outside_the_flat_shape_is_refused(self):
        for text in ("a:\n  b: 1\n", "a: [1, 2]\n", "a: bare words\n", "- a\n", "a: 1\na: 2\n"):
            with self.subTest(text=text):
                self.assertIn("x.yaml", self.refused(text))

    def test_yaml_version_is_refused(self):
        self.assertIn("version", self.refused("version: 12\n"))


class PackModConfig(unittest.TestCase):
    """The pack's own overrides against the mods they are for."""

    def test_every_file_is_for_a_locked_mod(self):
        config = packtool.collect_mod_config()
        ids = {m["id"] for m in packtool.load_lock()["mods"]}
        for name in config:
            if name.endswith(".yaml"):
                self.assertIn(Path(name).stem, ids, name)
        if any(name.endswith(".yaml") for name in config):
            self.assertIn("configkit", ids, "nothing reads a .yaml config without ConfigKit")

    def test_yaml_keys_are_settings_the_mod_declares(self):
        # Cairn reports an unknown key as missing and sets nothing, so a typo would ship
        # as the mod's default. Needs the mod zips: skipped until `packtool fetch` has run.
        lock = {m["id"]: m for m in packtool.load_lock()["mods"]}
        for name, values in packtool.collect_mod_config().items():
            if not name.endswith(".yaml"):
                continue
            domain = Path(name).stem
            zip_path = ROOT / "build" / "mods" / lock[domain]["fileName"]
            if not zip_path.exists():
                self.skipTest(f"{zip_path.name} not fetched")
            with zipfile.ZipFile(zip_path) as z:
                schema = json.loads(z.read(f"assets/{domain}/config/configlib-patches.json"))
            declared = {s["name"]: s for group in schema["settings"].values() for s in group.values()}
            for key, value in values.items():
                with self.subTest(file=name, key=key):
                    self.assertIn(key, declared)
                    limits = declared[key].get("range")
                    if limits:
                        self.assertTrue(limits["min"] <= value <= limits["max"], limits)


if __name__ == "__main__":
    unittest.main()
