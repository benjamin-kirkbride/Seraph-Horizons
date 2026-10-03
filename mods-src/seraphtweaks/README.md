# Seraph Horizons Tweaks

A code mod holding the pack's own tweaks: gameplay changes to other mods, and Tidy Variants, which
tidies the creative inventory and the handbook. These are choices for this pack, not bug fixes, so
they live together here and not in a mod each. Every tweak has its own switch in
`ModConfig/seraphtweaks.json` (all on by default). A tweak whose mod is not installed is skipped.
One whose mod has changed shape logs a warning and leaves that mod alone.

Only the game's own assemblies are referenced at build time: each tweak to another mod finds what
it patches by name, so the mod builds from the game alone (`mod-release.yml` needs nothing else).

`"side": "Universal"`, required on the client. The server does the boiler behavior and feeds the
creative steam source; Tidy Variants runs on the client. The client needs the mod because the
steam source is a block with its own classes: the game cannot build a block whose class it does not
know, so a client without the mod could not join a server that has it (a server with the steam
source switched off, or without ppex, has no such block).

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

### Creative steam source (`CreativeSteamSource`)

Pipes and Power Expanded (`ppex`). A creative-only block, `seraphtweaks:creativesteamsource` ("Steam
source (Creative)"), that keeps every ppex pipe connected to it full of steam: the steam
counterpart of the game's creative auto rotor, for testing engines and pipe layouts without a
boiler. It is a plain cube with no texture of its own (the game's missing-texture look), in the
General and Pipes and Power Expanded creative tabs. It has no recipe, drops nothing and is left
out of the handbook.

It is set up the way the auto rotor is, by right-clicking it (the block's info shows both
settings):

- right-click: raise the output pressure, Ctrl+right-click: lower it (1 to 10 atm, from 3);
- Shift+right-click: raise the flow rate, Ctrl+Shift+right-click: lower it (0 to 100 L/s in
  steps of 10, from 30; 0 is off).

Each setting wraps around at its ends, as the auto rotor's do. Place a pipe against any face of
the block with a connector pointing at it (placing the pipe by clicking the block does that). Each
connected pipe takes its share of the flow, as steam at the set pressure, through ppex's own
`IPipeNode.TryProduce` on the pipe: the network fills until it reaches that pressure, never past
the weakest pipe's burst pressure, exactly as from a boiler. At or above a pipe's burst pressure
(iron 5 atm, steel 10 atm by ppex's defaults) ppex's over-pressure timer bursts a pipe as usual,
and a leaking network is capped at 1 atm as ppex caps any source.

Without ppex, or with the switch off, the blocktype is disabled before the game loads it, so the
block does not exist at all. If ppex has changed shape (`IPipeNode.TryProduce(float, float,
string, float, bool)` or exlib's `BlockNetworkNode.HasConnectorAt(BlockFacing)` is gone) the mod
logs a warning and the block is left out the same way.

### Age of Flax rebalance (`AgeOfFlaxRebalance`)

Age of Flax (fork) (`ageofflaxfork` 1.1.6, asset domain `ageofflax`). Ripe flax drops flax bundles
(avg 1.2, Age of Flax's), which go through the ripple (grain), a barrel (retting), drying, the break
and the hatchel (fibers). Each tool comes in three tiers, which process 1, 2 and 4 bundles per use.
This tweak:

- **seeds**: the flax plant drops vanilla flax's seeds again, avg 1.2 when ripe (stage 9) and avg
  0.7 at stage 8, on top of Age of Flax's bundles (avg 1.2 and 0.5); stages below 8 drop Age of
  Flax's seeds as before. The ripple drops no seeds at any tier.
- **yields**: the ripple's grain and the hatchel's fibers per ripe plant are 2/3 of vanilla flax's
  (grain avg 3, fibers avg 4) with primitive tools, the same with simple ones and 4/3 with advanced
  ones:

  | tier | grain per bundle | grain per ripe plant | fibers per bundle | fibers per ripe plant |
  | --- | --- | --- | --- | --- |
  | primitive | 1.67 (±0.25) | 2 | 2.22 (±0.35) | 2.67 |
  | simple | 2.5 (±0.4) | 3 | 3.33 (±0.5) | 4 |
  | advanced | 3.33 (±0.5) | 4 | 4.44 (±0.65) | 5.33 |

  Age of Flax rolls once per use (a Gaussian of avg and the ± as its standard deviation, rounded at
  random) and multiplies by the bundles processed, so the mean is exact; the spreads are small
  enough that a roll below zero, which would drop nothing and raise the mean, practically never
  happens. Was: seeds 1.2 / 2.1 / 2.5 and grain 6 / 8 / 12 per bundle, fibers 5 / 6 / 8.
- **steel**: the advanced ripple, hatchel and break take steel nails and strips (and the break a
  steel rod) instead of iron.
- **fat**: every break takes raw or rendered fat (`game:fat*`), as the ModDB mod
  [Age of Flax Fork Break Patch](https://mods.vintagestory.at/ageofflaxforkbreakpatch)
  (`ageofflaxforkbreakpatch` 1.0.0) does; its only content is that patch to `breaks.json`.
- **text**: Age of Flax's English text (the only language it ships) says all of this: the seeds come
  from the plant and the ripple strips the grain (the guide, the unprocessed bundle and the ripples),
  the guide gives each tier's yields and materials, and the drying rack dries 3x faster, its real
  `dryingRackSpeedMultiplier` (the text said 2x). It also puts right the dried and broken bundles'
  descriptions, which a duplicate key in Age of Flax's lang file mixed up, and two "Extact" typos.

The yields and recipes are JSON patches in `assets/seraphtweaks/patches/ageofflax-*.json`, each
`dependsOn` ageofflaxfork. Age of Flax reads `ageofflax:config/balance.json` again in each tool's
and the crop's `OnLoaded`, after the patch loader has run, so the patched values are the ones used.
With the switch off (or Age of Flax changed) the system empties those patch files in `Start`, before
the patch loader runs in `AssetsLoaded`. Age of Flax's `BlockCropFlax.GetDrops` drops only bundles
at stages 8 and 9 and never reads the blocktype's drops, so the seeds come from a Harmony postfix on
it (server side); the same seeds are added to the flax blocktype's `*-8` and `*-9` drops, which the
handbook lists. The text edits are `LangEdits` exact-passage edits, as for ppex (`LangText`); the
ripple and drying rack descriptions are wildcard keys, so they are set on each variant's own key.
The switch is read on each side: a client with it off keeps Age of Flax's own text.

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

The same class places the creative steam source against a closed iron pipe and requires the pipe
full of steam at the set pressure, and no higher; it also requires the block in the creative
inventory with no drops and no recipe. `CreativeSteamSourceOffScenarios` boots a server with the
switch off and requires the block not to exist.

`tests/PackTests/AgeOfFlaxRebalanceScenarios.cs` (Atlas) reads the loaded ripples' and hatchels'
yields (what the tools use, set from the patched balance file), requires steel and no iron in the
advanced recipes and both fats in every break, rolls a flax plant's drops at stages 9, 8 and 5 on
farmland (seeds and bundles), checks the seeds in the blocktype's drops, and requires every
`LangEdits` passage reworded. When it fails after an Age of Flax update, match the patches and edits
to the new files. `AgeOfFlaxRebalanceOffScenarios` boots a server with the switch off and requires
Age of Flax as it ships.

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
