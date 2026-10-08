"""tools/configdefaults.py: the config defaults snapshots (docs/config-defaults.md).

The values the pack sets (pack/config/ModConfig), the owners of each file, the snapshot as it is
built from a captured ModConfig and compared with the committed one, and the committed snapshot of
the current pack version against the lock, the pack values and the renames, offline. Whether the
snapshot matches what a server writes is CI's smoke job (`packtool smoke --config-defaults check`).

Run with `python3 -m unittest discover -s tools/tests`.
"""

import importlib.util
import json
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))
import configdefaults  # noqa: E402

_spec = importlib.util.spec_from_file_location("packtool", ROOT / "tools" / "packtool.py")
packtool = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(packtool)


def write_tree(base: Path, files: dict) -> None:
    for name, text in files.items():
        path = base / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(text if isinstance(text, bytes) else text.encode())


class PackValues(unittest.TestCase):
    def values(self, files: dict) -> list:
        with tempfile.TemporaryDirectory() as tmp:
            write_tree(Path(tmp), files)
            return configdefaults.pack_values(Path(tmp))

    def test_json_leaves_and_yaml_lines_with_the_values_text(self):
        got = self.values({
            "a.json": '{"list": ["b"], "nested": {"n": 1.5}}',
            "mod.yaml": "# why\n\nmin_distance: 1200\nchance: 1.5\non: true\nname: \"x y\"\n",
        })
        self.assertEqual(got, [
            {"file": "a.json", "path": ["list"], "value": '["b"]'},
            {"file": "a.json", "path": ["nested", "n"], "value": "1.5"},
            {"file": "mod.yaml", "path": ["min_distance"], "value": "1200"},
            {"file": "mod.yaml", "path": ["chance"], "value": "1.5"},
            {"file": "mod.yaml", "path": ["on"], "value": "true"},
            {"file": "mod.yaml", "path": ["name"], "value": '"x y"'},
        ])

    def test_yaml_outside_the_flat_shape_is_refused(self):
        for text in ("a:\n  b: 1\n", "a: [1, 2]\n", "a: bare words\n", "- a\n", "a: 1\na: 2\n"):
            with self.subTest(text=text), self.assertRaises(configdefaults.ConfigDefaultsError):
                self.values({"x.yaml": text})

    def test_version_is_refused(self):
        for files in ({"x.yaml": "version: 12\n"}, {"x.json": '{"Version": 2}'}):
            with self.subTest(files=files), self.assertRaisesRegex(configdefaults.ConfigDefaultsError, "version"):
                self.values(files)

    def test_other_files_are_refused(self):
        with self.assertRaises(configdefaults.ConfigDefaultsError):
            self.values({"notes.txt": "x"})


class PackModConfig(unittest.TestCase):
    """The pack's own values against the mods they are for."""

    def test_every_yaml_file_is_for_a_locked_mod_read_by_configkit(self):
        ids = {m["id"] for m in packtool.load_lock()["mods"]}
        names = {v["file"] for v in configdefaults.pack_values()}
        for name in names:
            if name.endswith(".yaml"):
                self.assertIn(Path(name).stem, ids, name)
        if any(n.endswith(".yaml") for n in names):
            self.assertIn("configkit", ids, "nothing reads a .yaml config without ConfigKit")

    def test_yaml_keys_are_settings_the_mod_declares(self):
        # A typo would ship as a key no mod reads. Needs the mod zips: skipped until `packtool
        # fetch` has run.
        lock = {m["id"]: m for m in packtool.load_lock()["mods"]}
        for v in configdefaults.pack_values():
            if not v["file"].endswith(".yaml"):
                continue
            domain = Path(v["file"]).stem
            zip_path = ROOT / "build" / "mods" / lock[domain]["fileName"]
            if not zip_path.exists():
                self.skipTest(f"{zip_path.name} not fetched")
            with zipfile.ZipFile(zip_path) as z:
                schema = json.loads(z.read(f"assets/{domain}/config/configlib-patches.json"))
            declared = {s["name"]: s for group in schema["settings"].values() for s in group.values()}
            key, value = v["path"][0], configdefaults.yaml_value(v["value"])
            with self.subTest(file=v["file"], key=key):
                self.assertIn(key, declared)
                limits = declared[key].get("range")
                if limits:
                    self.assertTrue(limits["min"] <= value <= limits["max"], limits)

    def test_the_cairn_pack_carries_no_mod_config(self):
        meta = {"id": "p", "name": "P", "version": "1", "game_version": "1.22.7"}
        self.assertNotIn("modConfig", packtool.cairn_bundle(meta, {"mods": []})["pack"])


class Owners(unittest.TestCase):
    MODS = {"carryon": "1", "carryonlib": "1", "configkit": "1", "betterruins": "1", "exlib": "1",
            "hydrateordiedrate": "1", "slowtox": "1", "slowtoxvisualized": "1", "walkingstick": "1",
            "stonequarryrepckfipil": "1"}

    def test_by_name(self):
        ids = sorted(self.MODS)
        self.assertEqual(configdefaults.guess_owner("CarryOnConfig.json", ids), "carryon")
        self.assertEqual(configdefaults.guess_owner("SlowTox.json", ids), "slowtox")
        self.assertEqual(configdefaults.guess_owner("WalkingSticks/walkingstick-gas.json", ids), "walkingstick")
        self.assertEqual(configdefaults.guess_owner("stonequarry.json", ids), "stonequarryrepckfipil")
        self.assertIsNone(configdefaults.guess_owner("HoD.AddCooling.json", ids))

    def test_by_hand_configkit_code_and_name(self):
        blobs = {"hydrateordiedrate": "x HoD.AddCooling.json y".encode("utf-16-le"),
                 "exlib": "ex_values.json".encode("utf-16-le")}
        owners, unplaced = configdefaults.find_owners(
            ["HoD.AddCooling.json", "betterruins.yaml", "CarryOnConfig.json", "ex_values.json", "mystery.json"],
            blobs, self.MODS, {"mystery.json": ["exlib"]})
        self.assertEqual(owners, {
            "HoD.AddCooling.json": ["hydrateordiedrate"],
            "betterruins.yaml": ["betterruins", "configkit"],
            "CarryOnConfig.json": ["carryon"],
            "ex_values.json": ["exlib"],
            "mystery.json": ["exlib"],
        })
        self.assertEqual(unplaced, [])
        _, unplaced = configdefaults.find_owners(["mystery.json"], {}, self.MODS, {})
        self.assertEqual(unplaced, ["mystery.json"])


class Snapshot(unittest.TestCase):
    def test_build_write_read_and_compare(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            write_tree(tmp / "ModConfig", {
                "seraphhorizons.json": '{"A": 1}',
                "betterruins.yaml": b"version: 12\r\nlargeruins_min_spawn_distance: 2500\r\n",
                "seraphhorizons-configdefaults.json": '{"PackVersion": "0.1.0"}',
            })
            (tmp / "Mods").mkdir()
            index, files, problems = configdefaults.build(tmp / "ModConfig", tmp / "Mods", "9.9.9")
            self.assertNotIn("seraphhorizons-configdefaults.json", files)  # ignored
            self.assertEqual(files["betterruins.yaml"], b"version: 12\r\nlargeruins_min_spawn_distance: 2500\r\n")
            self.assertEqual(index["files"]["betterruins.yaml"], {"format": "yaml", "owners": ["betterruins", "configkit"]})
            self.assertEqual(index["files"]["seraphhorizons.json"], {"format": "json", "owners": ["seraphhorizons"]})
            self.assertEqual(index["packVersion"], "9.9.9")
            # The pack's betterruins values name settings this trimmed file does not have.
            self.assertTrue(any("has no largeruins_min_distance" in p for p in problems), problems)

            root = tmp / "snapshots"
            configdefaults.write_snapshot(index, files, root)
            back = configdefaults.read_snapshot("9.9.9", root)
            self.assertEqual(configdefaults.differences((index, files), back), [])
            changed = {**files, "seraphhorizons.json": b'{"A": 2}'}
            diffs = configdefaults.differences((index, changed), back)
            self.assertEqual(len(diffs), 1)
            self.assertIn("seraphhorizons.json: differs", diffs[0])
            self.assertEqual(configdefaults.differences((index, files), None),
                             ["no snapshot is committed for this pack version"])


class CommittedSnapshot(unittest.TestCase):
    """The current pack version's snapshot, offline: what it records besides the files."""

    def setUp(self):
        self.version = configdefaults.pack_version()
        self.snap = configdefaults.read_snapshot(self.version)
        self.assertIsNotNone(self.snap, f"no snapshot for pack {self.version}; run `{configdefaults.REGENERATE}`")

    def test_records_the_locked_mods_unless_released(self):
        if configdefaults.released(self.version):
            self.skipTest(f"pack {self.version} is released: its snapshot is frozen")
        index, _ = self.snap
        self.assertEqual(index["mods"], configdefaults.pack_mods(),
                         f"the lock changed; run `{configdefaults.REGENERATE}`")

    def test_records_the_pack_values_and_renames_unless_released(self):
        if configdefaults.released(self.version):
            self.skipTest(f"pack {self.version} is released: its snapshot is frozen")
        index, _ = self.snap
        self.assertEqual(index["packValues"], configdefaults.pack_values(),
                         f"pack/config/ModConfig changed; run `{configdefaults.REGENERATE}`")
        self.assertEqual(index["renames"], configdefaults.load_settings()["renames"],
                         f"pack/config-defaults.toml's renames changed; run `{configdefaults.REGENERATE}`")

    def test_index_and_files_agree(self):
        index, files = self.snap
        self.assertEqual(sorted(index["files"]), sorted(files))
        self.assertEqual(index["format"], configdefaults.INDEX_FORMAT)
        for name, meta in index["files"].items():
            self.assertTrue(meta["owners"], name)
            for owner in meta["owners"]:
                self.assertIn(owner, index["mods"], name)

    def test_every_version_is_a_folder_with_an_index(self):
        for d in configdefaults.SNAPSHOTS.iterdir():
            self.assertTrue((d / "index.json").exists(), d)
            self.assertEqual(json.loads((d / "index.json").read_text())["packVersion"], d.name)


if __name__ == "__main__":
    unittest.main()
