"""packtool assemble's Cairn pack file, and the pack's own mod, which it fetches by address.

CI's cairn job assembles with `--label next --url-mod seraphhorizons_next_<sha7>.zip <url>` (the build's zip,
named after the commit so that the address changes with every build: Cairn notices a changed
address or version, never a changed hash), so the `next` pack carries the pack's own mod built
from the same commit. The entries follow Cairn 0.9.10
(cairn-app src/Cairn.Core/Packs/PackManifest.cs): the manifest names the address and no
version, the lock holds the zip's sha256 with `fromUrl`, and there is one entry per modid.
Versioned releases (release.yml) do the same with `seraphhorizons_<v>.zip`, the release's own
asset. The server bundle carries that zip as a file; the mod list stays ModDB-only. There is no
meta-mod. `--label next` puts `next` in the file names in place of the version, and nothing else.

Run with `python3 -m unittest discover -s tools/tests`.
"""

import argparse
import contextlib
import hashlib
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

URL = "https://github.com/owner/repo/releases/download/next/seraphhorizons_next_abc1234.zip"


def make_zip(dir: Path, name: str = "seraphhorizons_next_abc1234.zip", **info) -> Path:
    info = {"type": "code", "modid": "seraphhorizons", "version": "1.2.3", "side": "Universal", **info}
    path = dir / name
    with zipfile.ZipFile(path, "w") as z:
        z.writestr("modinfo.json", json.dumps({k: v for k, v in info.items() if v is not None}))
        z.writestr("SeraphHorizons.dll", b"not really a dll")
    return path


def refuses(fn, *args):
    with contextlib.redirect_stderr(io.StringIO()) as err, unittest.TestCase().assertRaises(SystemExit):
        fn(*args)
    return err.getvalue()


def assemble(out: Path, cache: Path, url_mod=None, label=None) -> None:
    """cmd_assemble with no redistributable zips, which would need the fetch cache."""
    lock = packtool.load_lock()
    real_load_lock = packtool.load_lock
    packtool.load_lock = lambda: {**lock, "mods": [{**m, "redistribute": False} for m in lock["mods"]]}
    try:
        with contextlib.redirect_stdout(io.StringIO()):
            packtool.cmd_assemble(argparse.Namespace(out=str(out), cache=str(cache), url_mod=url_mod, label=label))
    finally:
        packtool.load_lock = real_load_lock


class UrlMod(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)

    def tearDown(self):
        self._tmp.cleanup()

    def test_reads_the_zip(self):
        z = make_zip(self.tmp)
        self.assertEqual(packtool.url_mod(z, URL), {
            "modid": "seraphhorizons", "version": "1.2.3", "filename": z.name, "url": URL,
            "sha256": hashlib.sha256(z.read_bytes()).hexdigest(), "side": "both",
        })

    def test_the_version_is_modinfo_jsons_not_the_file_names(self):
        # next's zip carries the commit in its name only; Cairn and the game read modinfo.json.
        z = make_zip(self.tmp, name="seraphhorizons_next_0123abc.zip")
        url = URL.rsplit("/", 1)[0] + "/" + z.name
        m = packtool.url_mod(z, url)
        self.assertEqual((m["version"], m["filename"], m["url"]), ("1.2.3", z.name, url))
        # The address must still end in this very file's name, commit and all.
        refuses(packtool.url_mod, z, URL)

    def test_refuses_an_address_cairn_or_players_should_not_fetch_from(self):
        z = make_zip(self.tmp)
        for url in ("http://127.0.0.1:8000/seraphhorizons_next_abc1234.zip",
                    "http://example.com/seraphhorizons_next_abc1234.zip",
                    "https://example.com/seraphhorizons_next_abc1234.zip?x=1",
                    "https://example.com/other_1.2.3.zip",
                    "https://example.com/download"):
            with self.subTest(url=url):
                refuses(packtool.url_mod, z, url)

    def test_refuses_a_zip_that_is_not_a_mod(self):
        refuses(packtool.url_mod, self.tmp / "missing.zip", URL)
        bad = self.tmp / "seraphhorizons_next_abc1234.zip"
        bad.write_bytes(b"<html>sign in</html>")
        refuses(packtool.url_mod, bad, URL)
        with zipfile.ZipFile(bad, "w") as z:
            z.writestr("readme.txt", "no modinfo")
        refuses(packtool.url_mod, bad, URL)
        refuses(packtool.url_mod, make_zip(self.tmp, version=None), URL)


class CairnBundle(unittest.TestCase):
    META = {"id": "seraphhorizons", "name": "Seraph Horizons", "game_version": "1.22.7"}
    LOCK = {"mods": [{"id": "exlib", "version": "1.0.0", "fileName": "exlib.zip", "fileUrl": "https://x/exlib.zip",
                      "releaseId": 1, "fileId": 2, "sha256": "ab" * 32, "side": "universal"}]}
    URL_MOD = {"modid": "seraphhorizons", "version": "1.2.3", "filename": "seraphhorizons_next_abc1234.zip",
               "url": URL, "sha256": "cd" * 32, "side": "both"}

    def test_without_url_mods_only_moddb_pins(self):
        b = packtool.cairn_bundle(self.META, self.LOCK)
        self.assertEqual(b["pack"]["mods"], [{"modid": "exlib", "version": "1.0.0"}])
        self.assertEqual([m["modid"] for m in b["lock"]["mods"]], ["exlib"])
        self.assertNotIn("fromUrl", json.dumps(b))

    def test_url_mod_is_an_address_in_the_manifest_and_a_hash_in_the_lock(self):
        b = packtool.cairn_bundle(self.META, self.LOCK, [self.URL_MOD])
        # No version beside the address: Cairn refuses the two together (pack-mod-url-and-pin).
        self.assertEqual(b["pack"]["mods"][-1], {"modid": "seraphhorizons", "url": URL})
        self.assertEqual(b["lock"]["mods"][-1], {
            "modid": "seraphhorizons", "version": "1.2.3", "filename": "seraphhorizons_next_abc1234.zip",
            "url": URL, "sha256": "cd" * 32, "fromUrl": True, "side": "both",
        })
        self.assertEqual(b["pack"]["mods"][0], {"modid": "exlib", "version": "1.0.0"})

    def test_two_url_mods_with_one_modid_are_refused(self):
        refuses(packtool.cairn_bundle, self.META, self.LOCK, [self.URL_MOD, {**self.URL_MOD, "modid": "SeraphHorizons"}])

    def test_url_mod_replaces_the_moddb_pin_of_its_modid(self):
        # The day the pack's own mod is pinned from the ModDB: next ships the commit's build,
        # versioned releases (no url_mods) the pin.
        pinned = {"mods": self.LOCK["mods"] + [{**self.LOCK["mods"][0], "id": "seraphhorizons", "version": "1.0.0",
                                                "sha256": "ef" * 32}]}
        b = packtool.cairn_bundle(self.META, pinned, [{**self.URL_MOD, "modid": "SeraphHorizons"}])
        self.assertEqual(b["pack"]["mods"], [{"modid": "exlib", "version": "1.0.0"},
                                             {"modid": "SeraphHorizons", "url": URL}])
        self.assertEqual([(m["modid"], m["sha256"], m.get("fromUrl")) for m in b["lock"]["mods"]],
                         [("exlib", "ab" * 32, None), ("SeraphHorizons", "cd" * 32, True)])
        plain = packtool.cairn_bundle(self.META, pinned)
        self.assertEqual(plain["pack"]["mods"][-1], {"modid": "seraphhorizons", "version": "1.0.0"})
        self.assertEqual(plain["lock"]["mods"][-1]["sha256"], "ef" * 32)
        self.assertNotIn("fromUrl", json.dumps(plain))


class Assemble(unittest.TestCase):
    def test_writes_a_dot_cairn_file_and_only_it_carries_the_url_mod(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            meta = packtool.load_pack()["pack"]
            tag = f"{meta['id']}_{meta['version']}"
            z = make_zip(tmp, modid="seraphhorizonstestmod")
            assemble(tmp / "plain", tmp / "cache")
            assemble(tmp / "next", tmp / "cache", [[str(z), URL]], label="next")
            # No meta-mod, no .cairn.json; next's files say next where a release's say the version.
            for d, t in (("plain", tag), ("next", f"{meta['id']}_next")):
                self.assertEqual(sorted(p.name for p in (tmp / d).iterdir()),
                                 sorted([f"{t}.cairn", f"{t}_modlist.txt", f"{t}_server.zip", "SHA256SUMS"]))
            plain = json.loads((tmp / "plain" / f"{tag}.cairn").read_text())
            nxt = json.loads((tmp / "next" / f"{meta['id']}_next.cairn").read_text())
            tag_next = f"{meta['id']}_next"
            self.assertFalse(any("url" in m for m in plain["pack"]["mods"]))
            self.assertEqual(nxt["pack"]["mods"][:-1], plain["pack"]["mods"])
            self.assertEqual(nxt["pack"]["mods"][-1], {"modid": "seraphhorizonstestmod", "url": URL})
            self.assertEqual(nxt["lock"]["mods"][-1]["sha256"], hashlib.sha256(z.read_bytes()).hexdigest())
            # The server bundle carries the zip itself; the mod list stays ModDB-only.
            with zipfile.ZipFile(tmp / "next" / f"{tag_next}_server.zip") as a, \
                    zipfile.ZipFile(tmp / "plain" / f"{tag}_server.zip") as b:
                self.assertEqual(a.read(f"Mods/{z.name}"), z.read_bytes())
                self.assertEqual(sorted(a.namelist()), sorted(b.namelist() + [f"Mods/{z.name}"]))
                # The label is in the file names only: the bundled lock keeps the version.
                self.assertEqual(json.loads(a.read("lock.json"))["pack"]["version"], meta["version"])
            self.assertEqual((tmp / "next" / f"{tag_next}_modlist.txt").read_text(),
                             (tmp / "plain" / f"{tag}_modlist.txt").read_text())

    def test_the_server_bundle_fetches_no_pin_a_url_mod_replaces(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            meta = packtool.load_pack()["pack"]
            tag = f"{meta['id']}_{meta['version']}"
            pinned = packtool.load_lock()["mods"][0]
            z = make_zip(tmp, name=f"{pinned['id']}_9.9.9.zip", modid=pinned["id"], version="9.9.9")
            assemble(tmp / "dist", tmp / "cache", [[str(z), URL.rsplit("/", 1)[0] + "/" + z.name]])
            with zipfile.ZipFile(tmp / "dist" / f"{tag}_server.zip") as a:
                script = a.read("fetch-mods.sh").decode()
                self.assertIn(f"Mods/{z.name}", a.namelist())
            self.assertNotIn(pinned["fileName"], script)
            self.assertIn(packtool.load_lock()["mods"][1]["fileName"], script)

    def test_a_label_that_is_no_file_name_part_is_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            for label in ("", "a/b", "../x", "next build"):
                with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
                    assemble(tmp / "dist", tmp / "cache", label=label)
                self.assertFalse((tmp / "dist").exists())

    def test_a_bad_url_mod_writes_nothing(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            z = make_zip(tmp)
            with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
                assemble(tmp / "dist", tmp / "cache", [[str(z), "http://example.com/" + z.name]])
            self.assertFalse((tmp / "dist").exists())


if __name__ == "__main__":
    unittest.main()
