# Icons

Item and block icons for the recipe browser, rendered in the game client and imported with
`tools/icons.py`. Do not edit these files by hand.

- `index.json` maps item codes to image hashes: `{ "schemaVersion": 1, "size": 64, "icons": { "game:stick": "<sha256>" } }`.
- `<first two hex digits>/<sha256>.png` is one distinct image, named by the sha256 of the file.
  Pack versions share images. The PNGs are stored with Git LFS (see `.gitattributes`).

Run `python3 tools/icons.py verify` to check the store. To add or refresh icons, remove a mod's
icons, or set up LFS on a new clone, see [docs/recipe-browser/icons.md](../docs/recipe-browser/icons.md).

The icons show the base game and the pack's mods. The authors of those keep their rights. They
are shown on the unofficial, fan-made recipe browser with credits, and are removed on request.
