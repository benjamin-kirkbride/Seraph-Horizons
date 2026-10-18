"""seraphhorizons' ore processing handbook texts and the figures they quote.

The "Ore processing" guide and the ore forms' handbook sections in lang/en.json quote figures from
config/ore-processing.json and a few C# constants. `mods-src/seraphhorizons/Ore/tools/
make_ore_guide_numbers.py` fills them in from `ore-guide-numbers.template.json` beside it; these hold
en.json to that (the drift check CI runs) and the generator to its rules.

Run with `python3 -m unittest discover -s tools/tests -p test_ore_guide_numbers.py`.
"""

from __future__ import annotations

import io
import re
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from decimal import Decimal
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "mods-src" / "seraphhorizons" / "Ore" / "tools"))

import make_ore_guide_numbers as gen  # noqa: E402


class Drift(unittest.TestCase):
    def test_en_json_is_the_template_with_the_current_figures(self):
        err = io.StringIO()
        with redirect_stderr(err), redirect_stdout(io.StringIO()):
            code = gen.main(["--check"])
        self.assertEqual(0, code, err.getvalue())

    def test_every_figure_is_used_and_every_template_key_is_in_en_json(self):
        template = gen.load_template()
        used = {m for text in template.values() for m in gen.PLACEHOLDER.findall(text)}
        figs = gen.figures(gen.load_config(), gen.load_csharp())
        self.assertEqual(set(figs), used)
        lang = gen.LANG.read_text(encoding="utf-8")
        for key in template:
            gen._value_span(lang, key)

    def test_no_bare_figure_left_in_the_template(self):
        """A number in a covered text is a placeholder (the guide's '20 concentrate' is its example)."""
        for key, text in gen.load_template().items():
            bare = re.findall(r"\d+(?:\.\d+)?", gen.PLACEHOLDER.sub("", text).replace("20 concentrate", ""))
            self.assertEqual([], bare, key)


class Generator(unittest.TestCase):
    def figs(self, **changes):
        cfg = gen.load_config()
        for path, value in changes.items():
            node = cfg
            *parents, leaf = path.split("__")
            for p in parents:
                node = node[p]
            node[leaf] = Decimal(value)
        return gen.figures(cfg, gen.load_csharp())

    def test_percent_rounds_half_up_to_one_decimal(self):
        self.assertEqual("21.3", gen.pct(Decimal("0.25") * Decimal("0.85")))
        self.assertEqual("8.5", gen.pct(Decimal("0.085")))
        self.assertEqual("100", gen.pct(Decimal("1.0")))
        self.assertEqual("36.8", gen.pct(Decimal("0.368")))

    def test_the_figures_follow_the_config(self):
        f = self.figs(handStations__firepit="0.8", machines__reverberatory__recovery="0.9",
                      retort__mercuryReturn="0.5", bullion__richLeadSilver="0.25")
        self.assertEqual("80", f["firepit"])
        self.assertEqual("20", f["roasted.medium"])
        self.assertEqual("16", f["firepit.of20"])
        self.assertEqual("keeps 90 % of its metal", f["reverberatory.loses"])
        self.assertEqual("half", f["mercuryReturn"])
        self.assertEqual("a quarter", f["richLeadSilver"])

    def test_the_current_figures(self):
        f = self.figs()
        self.assertEqual("3, 5, 7 or 10", f["rawUnits"])
        self.assertEqual("600", f["roastPoint"])
        self.assertEqual(("20", "21"), (f["litharge.out"], f["litharge.in"]))
        self.assertEqual("a third", f["stallroaster.sulfur"])
        self.assertEqual("38", f["byProduct.galena_nativesilver"])

    def test_wording_that_holds_for_one_value_stops_the_script(self):
        with self.assertRaises(gen.FigureError):
            self.figs(machines__liquationfurnace__recovery="0.9")
        with self.assertRaises(gen.FigureError):
            self.figs(smelt__sponge="0.9")
        with self.assertRaises(gen.FigureError):
            self.figs(retort__mercuryReturn="0.37")

    def test_only_the_template_keys_change_and_the_rest_keeps_its_bytes(self):
        lang = '{\n  "a": "x",\n  "k": "old \\"one\\" 5 °C",\n  "z": "y"\n}\n'
        new, stale = gen.apply(lang, {"k": 'new "one" 6 °C\nline'})
        self.assertEqual(["k"], stale)
        self.assertEqual('{\n  "a": "x",\n  "k": "new \\"one\\" 6 °C\\nline",\n  "z": "y"\n}\n', new)
        self.assertEqual((new, []), gen.apply(new, {"k": 'new "one" 6 °C\nline'}))

    def test_check_names_the_stale_keys_and_the_command(self):
        with tempfile.TemporaryDirectory() as tmp:
            lang = Path(tmp) / "en.json"
            text = gen.LANG.read_text(encoding="utf-8")
            lang.write_text(text.replace("It smelts back to lead at a small loss, 20 to every 21",
                                         "It smelts back to lead at a small loss, 19 to every 21"), encoding="utf-8")
            _, stale = gen.generate(lang=lang)
            self.assertEqual(["oreproduct-litharge-handbook-text"], stale)

    def test_the_comment_stripper_leaves_strings(self):
        self.assertEqual({"a": "x // y", "b": 1}, __import__("json").loads(gen.strip_comments('// c\n{"a": "x // y", /* z */ "b": 1}')))


if __name__ == "__main__":
    unittest.main()
