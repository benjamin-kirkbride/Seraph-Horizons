"""packtool smoke: staging the run's mods, and the pack's own mod built from mods-src. No server
or .NET needed (the build is stubbed).

Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import subprocess
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

_spec = importlib.util.spec_from_file_location("packtool", Path(__file__).resolve().parent.parent / "packtool.py")
packtool = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(packtool)


def mod_zip(path: Path, modid: str, version: str = "1.0.0") -> Path:
    with zipfile.ZipFile(path, "w") as z:
        z.writestr("modinfo.json", json.dumps({"modid": modid, "version": version}))
    return path


class StageMods(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.src = Path(self.tmp.name) / "mods"
        self.dest = Path(self.tmp.name) / "Mods"
        self.src.mkdir()
        self.dest.mkdir()

    def tearDown(self):
        self.tmp.cleanup()

    def test_pinned_copy_of_the_pack_mod_is_left_out_by_modid(self):
        mod_zip(self.src / "seraphhorizons_1.0.0.zip", "seraphhorizons")
        mod_zip(self.src / "seraphhorizonsaddon_0.1.0.zip", "seraphhorizonsaddon")  # only its modid prefix is the pack mod's: stays
        mod_zip(self.src / "renamed.zip", "seraphhorizons")  # by modid, not file name
        mod_zip(self.src / "olla_1.2.0.zip", "olla")
        folder = self.src / "localmod"
        folder.mkdir()
        (folder / "modinfo.json").write_text('{\n  // a comment\n  "ModID": "localmod",\n}')
        dropped = packtool.stage_mods(self.src, self.dest, {"seraphhorizons"})
        self.assertEqual(dropped, ["renamed.zip", "seraphhorizons_1.0.0.zip"])
        self.assertEqual(sorted(p.name for p in self.dest.iterdir()),
                         ["localmod", "olla_1.2.0.zip", "seraphhorizonsaddon_0.1.0.zip"])
        self.assertTrue((self.dest / "localmod" / "modinfo.json").exists())

    def test_nothing_pinned_drops_nothing(self):
        mod_zip(self.src / "olla_1.2.0.zip", "olla")
        (self.src / "notes.txt").write_text("not a mod")
        self.assertEqual(packtool.stage_mods(self.src, self.dest, {"seraphhorizons"}), [])
        self.assertEqual(sorted(p.name for p in self.dest.iterdir()), ["notes.txt", "olla_1.2.0.zip"])

    def test_modid_is_matched_without_case(self):
        mod_zip(self.src / "seraphhorizons_0.9.0.zip", "SeraphHorizons", "0.9.0")
        self.assertEqual(packtool.stage_mods(self.src, self.dest, {"seraphhorizons"}),
                         ["seraphhorizons_0.9.0.zip"])
        self.assertEqual(list(self.dest.iterdir()), [])

    def test_a_folder_mod_of_the_modid_is_left_out_too(self):
        folder = self.src / "seraphhorizons"
        folder.mkdir()
        (folder / "modinfo.json").write_text(json.dumps({"ModID": "seraphhorizons", "version": "1"}))
        self.assertEqual(packtool.stage_mods(self.src, self.dest, {"seraphhorizons"}), ["seraphhorizons"])

    def test_broken_zips_and_non_mods_are_kept(self):
        with zipfile.ZipFile(self.src / "broken.zip", "w") as z:
            z.writestr("x.dll", "x")  # no modinfo.json
        (self.src / "notzip.zip").write_text("not a zip")
        self.assertEqual(packtool.stage_mods(self.src, self.dest, {"seraphhorizons"}), [])
        self.assertEqual(sorted(p.name for p in self.dest.iterdir()), ["broken.zip", "notzip.zip"])


class StagePackMod(unittest.TestCase):
    def test_zip_is_named_after_modinfo(self):
        info = json.loads((packtool.PACK_MOD_PROJECT / "modinfo.json").read_text())
        self.assertEqual(info["modid"], packtool.PACK_MOD_ID)
        self.assertEqual(packtool.pack_mod_zip(),
                         packtool.ROOT / "build" / f"{info['modid']}_{info['version']}.zip")

    def test_builds_release_against_the_server_and_stages_the_zip_in_the_run_only(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            project = tmp / "mods-src" / "mymod"
            project.mkdir(parents=True)
            (project / "modinfo.json").write_text('{"modid": "mymod", "version": "2.0.0"}')
            data = tmp / "data"
            (data / "Mods").mkdir(parents=True)
            built = tmp / "build" / "mymod_2.0.0.zip"
            calls = []

            def fake_build(cmd, env):
                calls.append((cmd, env["VINTAGE_STORY"]))
                built.parent.mkdir(exist_ok=True)
                mod_zip(built, "mymod", "2.0.0")
                return subprocess.CompletedProcess(cmd, 0)

            with mock.patch.object(packtool, "ROOT", tmp), mock.patch.object(packtool.subprocess, "run", fake_build):
                staged = packtool.stage_pack_mod(tmp / "server", data, project)
            self.assertEqual(staged, data / "Mods" / "mymod_2.0.0.zip")
            self.assertTrue(staged.exists())
            cmd, server = calls[0]
            self.assertEqual(cmd[:5], ["dotnet", "build", str(project), "-c", "Release"])
            self.assertEqual(server, str(tmp / "server"))

    def test_a_stale_zip_does_not_hide_a_failed_build(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            project = tmp / "mymod"
            project.mkdir()
            (project / "modinfo.json").write_text('{"modid": "mymod", "version": "2.0.0"}')
            (tmp / "build").mkdir()
            mod_zip(tmp / "build" / "mymod_2.0.0.zip", "mymod", "2.0.0")  # from an earlier build
            with mock.patch.object(packtool, "ROOT", tmp), \
                    mock.patch.object(packtool.subprocess, "run", lambda cmd, env: subprocess.CompletedProcess(cmd, 1)), \
                    self.assertRaises(SystemExit):
                packtool.stage_pack_mod(tmp / "server", tmp / "data", project)


class SmokeArguments(unittest.TestCase):
    def test_there_is_no_local_mod_flag(self):
        """The pack's own mod is always built and staged; there is nothing to opt into."""
        with mock.patch.object(packtool, "cmd_smoke", lambda a: None), \
                mock.patch("sys.argv", ["packtool", "smoke", "--local-mod", "a.zip"]), \
                mock.patch("sys.stderr"), self.assertRaises(SystemExit):
            packtool.main()

    def test_smoke_parses_without_flags(self):
        seen = []
        with mock.patch.object(packtool, "cmd_smoke", seen.append), mock.patch("sys.argv", ["packtool", "smoke"]):
            packtool.main()
        self.assertFalse(hasattr(seen[0], "local_mod"))


if __name__ == "__main__":
    unittest.main()
