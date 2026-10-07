#!/usr/bin/env python3
"""Rescales the trade lists' buying prices to the trader's buy spread (2026-10-06, one-off).

    python3 mods-src/seraphhorizons/Trading/tools/rescale_buying.py [factor]

The lists' buying prices were on the value table's scale (0.94 x the table's value at the median).
A trader now pays a fifth of value (docs/trading.md, "Everything has a price"), and a list entry
holds the final pay, so every `price` under a list's top-level `buying` key has its `avg` and `var`
multiplied by `factor` (default 0.2). Selling prices are left alone. The files are edited in
place, number by number, so their layout and comments stay as they are, and a marker comment is
added under each file's header; a file that already carries it is skipped, so a second run does
nothing. The JSON is the source from then on: this script is kept for the record.
"""

import re
import sys
from pathlib import Path

LISTS = Path(__file__).resolve().parents[2] / "assets" / "seraphhorizons" / "config" / "tradelists"
MARKER = "// Buying prices are what the trader pays, a fifth of value (docs/trading.md, \"Everything has a price\")."
PRICE = re.compile(r'("price"\s*:\s*\{\s*"avg"\s*:\s*)(-?[0-9.]+)(\s*,\s*"var"\s*:\s*)(-?[0-9.]+)')


def number(x: float) -> str:
    """Shortest form to three decimals: 0.18, 2, 0.05."""
    s = f"{round(x, 3):.3f}".rstrip("0").rstrip(".")
    return "0" if s in ("", "-0") else s


def buying_span(text: str) -> tuple[int, int]:
    """Start and end offsets of the top-level "buying" object's value, skipping strings and // comments."""
    depth, i, n, key, start = 0, 0, len(text), None, -1
    while i < n:
        c = text[i]
        if text.startswith("//", i):
            i = text.find("\n", i)
            if i < 0:
                break
            continue
        if c == '"':
            j = i + 1
            while text[j] != '"':
                j += 2 if text[j] == "\\" else 1
            if depth == 1:
                key = text[i + 1:j]
            i = j + 1
            continue
        if c in "{[":
            depth += 1
            if depth == 2 and key == "buying" and c == "{":
                start = i
        elif c in "}]":
            depth -= 1
            if depth == 1 and start >= 0:
                return start, i + 1
        i += 1
    raise ValueError("no top-level buying object")


def rescale(text: str, factor: float) -> tuple[str, int]:
    start, end = buying_span(text)
    count = 0

    def sub(m: re.Match) -> str:
        nonlocal count
        count += 1
        return m.group(1) + number(float(m.group(2)) * factor) + m.group(3) + number(float(m.group(4)) * factor)

    body = PRICE.sub(sub, text[start:end])
    out = text[:start] + body + text[end:]
    # The marker goes after the leading comment block.
    lines = out.split("\n")
    k = 0
    while k < len(lines) and lines[k].startswith("//"):
        k += 1
    lines.insert(k, MARKER)
    return "\n".join(lines), count


def main() -> int:
    factor = float(sys.argv[1]) if len(sys.argv) > 1 else 0.2
    for path in sorted(LISTS.glob("trader-*.json")):
        text = path.read_text(encoding="utf-8")
        if MARKER in text:
            print(f"{path.name}: already rescaled, skipped")
            continue
        out, count = rescale(text, factor)
        path.write_text(out, encoding="utf-8")
        print(f"{path.name}: {count} buying prices x {factor}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
