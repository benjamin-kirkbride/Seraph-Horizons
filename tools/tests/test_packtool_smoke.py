"""packtool smoke --local-mod: which mods end up in the smoke run's Mods. No server needed.

Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import tempfile
import unittest
import zipfile
from unittest import mock
from pathlib import Path

_spec = importlib.util.spec_from_file_location("packtool", Path(__file__).resolve().parent.parent / "packtool.py")
packtool = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(packtool)


def mod_zip(path: Path, modinfo: dict | None) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w") as z:
        if modinfo is not None:
            z.writestr("modinfo.json", json.dumps(modinfo))
        z.writestr("x.dll", "x")
    return path


class StageLocalMods(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.mods = self.tmp / "Mods"
        self.mods.mkdir()
        self.built = self.tmp / "build"
        mod_zip(self.mods / "carryon_1.2.zip", {"modid": "carryon", "version": "1.2"})
        folder = self.mods / "seraphexport"
        folder.mkdir()
        (folder / "modinfo.json").write_text(json.dumps({"ModID": "seraphexport", "version": "1"}))

    def staged(self):
        return sorted(p.name for p in self.mods.iterdir())

    def test_no_local_mods_leaves_mods_alone(self):
        self.assertEqual(packtool.stage_local_mods(self.mods, []), {})
        self.assertEqual(self.staged(), ["carryon_1.2.zip", "seraphexport"])

    def test_unpinned_mod_is_added(self):
        z = mod_zip(self.built / "seraphhorizons_1.0.0.zip", {"modid": "seraphhorizons", "version": "1.0.0"})
        got = packtool.stage_local_mods(self.mods, [z])
        self.assertEqual(self.staged(), ["carryon_1.2.zip", "seraphexport", "seraphhorizons_1.0.0.zip"])
        self.assertEqual(got, {"seraphhorizons": "seraphhorizons 1.0.0 from seraphhorizons_1.0.0.zip"})
        self.assertTrue(z.exists(), "the build itself is copied, not moved")

    def test_pinned_zip_of_the_same_modid_is_replaced(self):
        mod_zip(self.mods / "seraphhorizons_0.9.0.zip", {"modid": "SeraphHorizons", "version": "0.9.0"})
        z = mod_zip(self.built / "seraphhorizons_1.0.0.zip", {"modid": "seraphhorizons", "version": "1.0.0"})
        got = packtool.stage_local_mods(self.mods, [z])
        self.assertEqual(self.staged(), ["carryon_1.2.zip", "seraphexport", "seraphhorizons_1.0.0.zip"])
        self.assertIn("in place of seraphhorizons_0.9.0.zip", got["seraphhorizons"])

    def test_pin_under_the_builds_own_name_is_replaced_by_the_build(self):
        mod_zip(self.mods / "seraphhorizons_1.0.0.zip", {"modid": "seraphhorizons", "version": "pinned"})
        z = mod_zip(self.built / "seraphhorizons_1.0.0.zip", {"modid": "seraphhorizons", "version": "1.0.0"})
        packtool.stage_local_mods(self.mods, [z])
        self.assertEqual(packtool.modinfo_from_zip(self.mods / "seraphhorizons_1.0.0.zip")["version"], "1.0.0")

    def test_folder_mod_of_the_same_modid_is_replaced(self):
        z = mod_zip(self.built / "seraphexport_2.zip", {"modid": "seraphexport", "version": "2"})
        packtool.stage_local_mods(self.mods, [z])
        self.assertEqual(self.staged(), ["carryon_1.2.zip", "seraphexport_2.zip"])

    def test_other_mods_and_non_mods_are_kept(self):
        (self.mods / "notes.txt").write_text("x")
        mod_zip(self.mods / "broken.zip", None)
        z = mod_zip(self.built / "seraphhorizons_1.0.0.zip", {"modid": "seraphhorizons", "version": "1.0.0"})
        packtool.stage_local_mods(self.mods, [z])
        self.assertEqual(self.staged(), ["broken.zip", "carryon_1.2.zip", "notes.txt", "seraphexport",
                                         "seraphhorizons_1.0.0.zip"])

    def test_missing_zip_dies(self):
        with self.assertRaises(SystemExit):
            packtool.stage_local_mods(self.mods, [self.built / "nope.zip"])

    def test_zip_without_modinfo_dies_before_anything_changes(self):
        good = mod_zip(self.built / "a.zip", {"modid": "carryon", "version": "2"})
        bad = mod_zip(self.built / "b.zip", None)
        with self.assertRaises(SystemExit):
            packtool.stage_local_mods(self.mods, [good, bad])
        self.assertEqual(self.staged(), ["carryon_1.2.zip", "seraphexport"])

    def test_same_modid_twice_dies(self):
        a = mod_zip(self.built / "a.zip", {"modid": "seraphhorizons", "version": "1"})
        b = mod_zip(self.built / "b.zip", {"modid": "SeraphHorizons", "version": "2"})
        with self.assertRaises(SystemExit):
            packtool.stage_local_mods(self.mods, [a, b])


class SmokeArguments(unittest.TestCase):
    def test_local_mod_is_repeatable(self):
        seen = []
        with mock.patch.object(packtool, "cmd_smoke", seen.append), \
                mock.patch("sys.argv", ["packtool", "smoke", "--local-mod", "a.zip", "--local-mod", "b.zip"]):
            packtool.main()
        self.assertEqual(seen[0].local_mod, ["a.zip", "b.zip"])

    def test_no_local_mod_by_default(self):
        seen = []
        with mock.patch.object(packtool, "cmd_smoke", seen.append), mock.patch("sys.argv", ["packtool", "smoke"]):
            packtool.main()
        self.assertIsNone(seen[0].local_mod)


if __name__ == "__main__":
    unittest.main()
