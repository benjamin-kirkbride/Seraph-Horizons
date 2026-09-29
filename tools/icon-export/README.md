# Icon export mod (seraphiconfix)

A client mod for exporting the recipe browser's item icons. It is a local tool: it is not in
`pack/pack.toml`, no release includes it, and players never install it. How to use it, and
why it exists, is in [docs/recipe-browser/icons.md](../../docs/recipe-browser/icons.md).

It does two things:

- Before the game's own `.blockitempngexport` and `.exponepng` draw (Ortho stage, order
  0.499), it makes the GUI shader active and resets the uniforms that switch textures off.
  With the full pack loaded the game's export otherwise writes white shapes.
- It adds `.seraphicons`, an export of its own that sets the GUI shader's state before every
  draw, spreads the work over frames, resumes, names files by their full code and writes a
  `manifest.json` that `tools/icons.py import` reads.

## Layout

```
IconExportFix.cs          the shader reset before the game's export
Game/                     everything that calls the game: the command, the drawing, diagnostics
Core/                     plain logic with no game types: file names, list files, manifest,
                          scheduling, the untextured check, command arguments
tests/                    xUnit tests for Core; need neither the game nor the pack
```

## Build

The mod references `VintagestoryAPI.dll` only, so the dedicated server's copy is enough:

```sh
VINTAGE_STORY=/path/to/vintagestory dotnet build tools/icon-export -c Release
```

A Release build also writes `build/seraphiconfix.zip` (the DLL and `modinfo.json`), which is
what goes into the game's mod folder.

## Test

```sh
dotnet test tools/icon-export/tests
```

The tests cover `Core/`. The drawing in `Game/` can only be tried in the game client; the
docs say how, and what to send back if it goes wrong.
