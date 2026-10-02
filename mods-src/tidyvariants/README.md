# Tidy Variants

A code mod that tames the creative inventory and the handbook. The pack's creative inventory has
about 27,000 entries, mostly variant multiplication (ores × rocks, doors × rocks × woods, foods ×
cooking states). From one set of rules, this mod:

- **hides** orientation and open/closed-state variants: gone from the creative and handbook lists,
  search included. `/giveitem` still works.
- **groups** everything else: one creative tile and one handbook page per group, showing a
  representative member. Alt+click a tile to expand the group inline; search runs over the flat
  list first, so it never loses an item.

Nothing decorative becomes unreachable from the UI. The design and its decisions are in #252.

## Layout

- `Core/`: the rule engine. Game-independent (the BCL only), so `tests/` compiles these files
  directly and runs without the game: `dotnet test mods-src/tidyvariants/tests`.
- `Game/`: the game-facing code (rule input from the loaded collectibles, the handbook's
  `groupBy`, the creative GUI patches). `TidyVariantsMod.cs` owns the Harmony lifecycle and
  applies every `[HarmonyPatch]` class in the assembly.
- `assets/tidyvariants/`: lang and config files, shipped as is (`lang/en.json` carries the group
  titles of override groups).

The mod is universal and required on both sides (`"side": "Universal"`): the logic runs on the
client, and the server requires it so Atlas can test rule resolution.

## Tests

- `tests/` (xunit, no game): the rule engine.
- `tests/PackTests/TidyVariantsScenarios.cs` (Atlas): the mod loads on a server with the whole
  pack. The test project loads this directory's build as a mod, and leaves out a pinned copy from
  the ModDB (`tidyvariants_*.zip` in `build/mods`).

## Releasing

The same as `mods-src/allowedvariantsfix/README.md`: bump the version in `modinfo.json` and
`TidyVariants.csproj`, merge, tag `tidyvariants-v<version>` on main, upload the zip from the
GitHub Release to the ModDB (keep the file name), then pin it in `pack/pack.toml` (the first
release adds the entry, `side = "universal"`) and run `packtool lock`.

For a local build: `dotnet build mods-src/tidyvariants -c Release` (needs `VINTAGE_STORY`) writes
`build/tidyvariants_<version>.zip`: the DLL and `modinfo.json` at the top level, and `assets/`.

## Credits

Some rules in the pack override file are ported from Handbook Declutterer (actioninja), which builds on
Fix Handbook Clutter (Craluminum2413), MIT. The notice and lineage are in `CREDITS.md`, which ships in
the mod zip.
