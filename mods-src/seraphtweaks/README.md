# Seraph Horizons Tweaks

A code mod holding the pack's own tweaks: gameplay changes to other mods, and Tidy Variants, which
tidies the creative inventory and the handbook. These are choices for this pack, not bug fixes, so
they live together here and not in a mod each. Every tweak has its own switch in
`ModConfig/seraphtweaks.json` (all on by default). A tweak whose mod is not installed is skipped.
One whose mod has changed shape logs a warning and leaves that mod alone.

Only the game's own assemblies are referenced at build time: each tweak to another mod finds what
it patches by name, so the mod builds from the game alone (`mod-release.yml` needs nothing else).

`"side": "Universal"`, not required on the client. The server does the boiler behavior; Tidy
Variants runs on the client. A client with the mod also gets the reworded text and Tidy Variants;
one without it sees vanilla text, a vanilla creative inventory and a vanilla handbook.

## Tweaks

### Boilers blow their lid instead of exploding (`BoilerLidBlowsOpen`)

Pipes and Power Expanded (`ppex`, 0.7.x). A boiler that keeps firing at its choke pressure for
`BoilerOverpressureSeconds` (ppex config, 30 s) calls `BlockEntityBoiler.Explode()`, which deletes
the boiler, drops a fraction of its materials and blasts the area. `BoilerLidRelief` prefixes
`Explode()` with the boiler's own `ToggleLid()` and skips the blast: the access lid swings open
with its sound and animation, and ppex vents steam through the open lid (`BoilerLidVentRate`,
200 L/s) and resets the over-pressure timer while it stays open. The player closes the lid by hand,
and a boiler still with nowhere to send its steam will blow it again.

The lid blowing open bangs like a ppex engine blowing up: it plays the sound ppex's
`BlockEntityEngine.Break()` plays, `game:sounds/effect/mediumexplosion` (ExpandedLib's
`ExSounds.MediumExplosion`), from the server at the boiler, over 24 blocks at half volume and
unrandomized pitch, as the engine does. The lid's own creak is still `ToggleLid()`'s.

The text that promised an explosion is reworded in place, in every language ppex ships (English,
Russian, Ukrainian): the over-pressure line in the boiler's info ("until the lid blows open!") and
the passages in the Steam Power and Boilers handbook pages. Each edit replaces one exact passage of
ppex's text (`LangEdits`). If ppex rewords it, that edit logs a warning and does nothing. Languages
other than the current one load lazily, so the mod loads those three when it starts.

### Fewer surface battle towers (ConfigKit settings)

Battle Towers (`battletowers`, 1.1.0) has no settings: a surface tower has a 0.03 chance per chunk
and only 200 blocks between two of them, so they outnumber every other surface structure.
`assets/seraphtweaks/config/configlib-patches.json` declares two settings, which ConfigKit writes
into Battle Towers' own patch file (`patches/survival-worldgen-structures.json`, entry 0) before
the game applies it:

| Setting (`ModConfig/seraphtweaks.yaml`) | Battle Towers | Here |
|---|---|---|
| `battletowers_surface_chance` | 0.03 | 0.01 |
| `battletowers_surface_min_distance` | 200 | 600 |

The hard and underground towers are left as Battle Towers ships them. This tweak is data, not a
class: it has no switch in `seraphtweaks.json`, does nothing without ConfigKit, and is changed in
ConfigKit's settings screen or, for the pack, in `pack/config/ModConfig/seraphtweaks.yaml`. Like
any worldgen setting it only affects chunks not generated yet.

### Tidy Variants (`TidyVariants`)

The pack's creative inventory has about 29,000 entries, mostly variant multiplication (ores ×
rocks, doors × rocks × woods, foods × cooking states). From one set of rules, Tidy Variants:

- **hides** orientation and open/closed-state variants: gone from the creative and handbook lists,
  search included. `/giveitem` still works.
- **groups** everything else: one creative tile and one handbook page per group, showing a
  representative member. Right-click a tile (or Ctrl+G over it) to expand the group inline, and
  right-click any of its members to collapse it; left-click still takes the item. Search runs over
  the flat list first, so it never loses an item.

Nothing decorative becomes unreachable from the UI. The design and its decisions are in #252, the
details in `docs/variant-grouping/` (creative inventory, handbook, game hooks, the Atlas report).

All of it acts on the client. The server resolves the same rules too, but only so the Atlas
scenarios and the report can read them. With the switch off nothing is resolved, patched or
changed, on either side. The creative inventory patches are applied by hand, on the client only,
under the Harmony id `seraphtweaks.creative`. Each client keeps its expanded groups in
`ModConfig/seraphtweaks-tidyvariants-creative.json`.

- `TidyVariants/Core/`: the rule engine. Game-independent (the BCL only), so `tests/` compiles
  these files directly and runs without the game. The format of the override file is in
  `TidyVariants/Core/README.md`.
- `TidyVariants/Game/`: the game-facing code (rule input from the loaded collectibles, the
  handbook's `groupBy`, the creative GUI patches) and the feature's mod systems.
- `assets/seraphtweaks/config/tidyvariants-overrides.json`: the pack's override rules, and
  `assets/seraphtweaks/lang/en.json`: their group titles and the feature's UI text. The feature's
  lang keys all start with `tidyvariants-` (`seraphtweaks:tidyvariants-group-...`).

Some rules in the override file are ported from Handbook Declutterer (actioninja), which builds
on Fix Handbook Clutter (Craluminum2413), MIT. The notice and lineage are in `CREDITS.md`, which
ships in the mod zip.

## Tests

`tests/` (xunit, no game): Tidy Variants' rule engine and the shipped override and lang files.
`dotnet test mods-src/seraphtweaks/tests`.

`tests/PackTests/SeraphTweaksScenarios.cs` (Atlas) places a Cornish boiler, calls `Explode()` with
the lid shut and with it open, and requires the boiler still standing with its lid open. It
requires `BlowSound` to be ExpandedLib's `ExSounds.MediumExplosion` and present in the assets. It also
requires every `LangEdits` passage reworded, and an edit set for every language ppex ships: when
either fails after a ppex update, match the edits to ppex's new text or add the new language.

It also reads the patched `game:worldgen/structures.json` and requires the surface tower's chance
and spacing above, with the hard tower's unchanged: when that fails after a Battle Towers update,
match the paths in `configlib-patches.json` to its new patch file.

`tests/PackTests/TidyVariants*Scenarios.cs` (Atlas) resolve the rules on a server with the whole
pack: every creative entry maps to its stack and back, the handbook layout keeps one listed page per
group, the creative and handbook systems stay off on the server, and the report
(`docs/variant-grouping/report.md`). `TidyVariantsOffScenarios` boots a server with the switch off.

The test project loads this directory's build as a mod, and leaves out a pinned copy from the
ModDB (`seraphtweaks_*.zip` in `build/mods`).

## Adding a tweak

Add a class next to `BoilerLidRelief.cs`, a `bool` setting for it in `SeraphTweaksConfig`, and
the call in `SeraphTweaksSystem` behind that setting. Then add scenarios and a section above. A
tweak big enough for mod systems of its own gets a folder, as `TidyVariants/` does; its systems
read their switch with `SeraphTweaksSystem.ConfigFor(api)`.

## Releasing

The same as `mods-src/allowedvariantsfix/README.md`: bump the version in `modinfo.json` and
`SeraphTweaks.csproj`, merge, tag `seraphtweaks-v<version>` on main, upload the zip from the
GitHub Release to the ModDB (keep the file name), then pin it in `pack/pack.toml` (the first
release adds the entry, `side = "universal"`) and run `packtool lock`.

For a local build: `dotnet build mods-src/seraphtweaks -c Release` (needs `VINTAGE_STORY`) writes
`build/seraphtweaks_<version>.zip`: the DLL, `modinfo.json` and `CREDITS.md` at the top level, and
`assets/`.
