# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A version-pinned Vintage Story modpack (game 1.22.7, .NET 10) plus a static **recipe browser** site built from data the pack's own server dumps. `README.md` covers installing and `CONTRIBUTING.md` layout and CI; `docs/recipe-browser/` and `docs/merge-queue.md` go deeper. Read the relevant doc before changing a subsystem: they record decisions and engine facts not obvious from the code.

## Commands

Server-dependent work (smoke, Atlas, building the C# mods) needs the .NET 10 SDK and a game server pointed to by `VINTAGE_STORY`: an extracted `vs_server_linux-x64_<ver>` archive, or a full client install of the same version.

```sh
# Pack (tools/packtool.py is stdlib-only Python 3.11+)
python3 tools/packtool.py lock        # after editing pack/pack.toml: resolve pins, rewrite pack/lock.json
uvx --from cogapp==3.6.0 cog -r README.md   # after adding/removing a mod or a game bump: README's status line is generated, and CI's lock job checks it
python3 tools/packtool.py check       # offline: pack.toml and lock.json agree
python3 tools/packtool.py fetch       # download + sha256-verify mods into build/mods (Atlas needs this first)
python3 tools/packtool.py smoke [--export build/recipes.json]   # boot a server, scan logs; --export also runs the recipe exporter
python3 tools/packtool.py outdated
python3 tools/packtool.py assemble    # build dist/ release artifacts

# Atlas scenarios (in-process headless server with every locked mod)
dotnet test tests/PackTests
dotnet test tests/PackTests --filter "FullyQualifiedName~RecipeExportScenarios"
# Every run leaves ~750 MB worlds in $TMPDIR/atlas (its last class, failures, killed runs); a /tmp
# tmpfs fills RAM, so locally run with TMPDIR on disk. StaleScratchSweep.cs deletes worlds idle > 2 h.
TMPDIR=~/.cache/atlas-tmp dotnet test tests/PackTests

# Pack-authored mods (mods-src/): Release writes build/<modid>_<version>.zip (CI does this on a <modid>-v<version> tag)
dotnet build mods-src/allowedvariantsfix -c Release
dotnet test mods-src/seraphhorizons/tests          # Tidy Variants rule engine (TidyVariants/Core/), needs no game

# Icon export mod (local-only, never shipped); its Core/ tests need no game
dotnet build tools/icon-export -c Release
dotnet test tools/icon-export/tests

# Python tool tests (install Pillow, or the icon import tests skip)
python3 -m unittest discover -s tools/tests -v
python3 -m unittest discover -s tools/tests -p test_icons.py   # one module
python3 tools/icons.py verify                        # icons/index.json matches the files (works on LFS pointers)

# tools/site-data (validate / migrate / assemble exports)
npm --prefix tools/site-data ci
npm --prefix tools/site-data run typecheck
npm --prefix tools/site-data test [-- test/migrate.test.ts]
npm --prefix tools/site-data run build && node tools/site-data/dist/bin.js validate path/to/recipes.json

# site/ (Vite + Svelte 5 + TS; Node >= 22.22)
npm --prefix site ci
npm --prefix site run check          # svelte-check + tsc, warnings fail
npm --prefix site test [-- test/search.test.ts]
RECIPE_EXPORT=path/to/recipes.json npm --prefix site run e2e    # Playwright; fails without a real export
npm --prefix site run prepare-data -- --export path/to/recipes.json --out site/public/data/main
npm --prefix site run dev
```

## Architecture

**Pack.** `pack/pack.toml` is the only hand-edited source of what's in the pack (exact pins, license, `redistribute`, and a `why` per mod). `packtool.py lock` derives `pack/lock.json` (URL + sha256) from it; never hand-edit the lock. Third-party mod files are never re-hosted; artifacts point at the ModDB CDN. The pack's own mod is the exception: it is released with the pack and its releases carry it (below). `pack/known-errors.json` lists tolerated cross-mod log errors and unresolved BetterRuins schematic blocks for both `smoke` and Atlas; every entry must link an issue, patterns must be valid in both Python `re` and .NET `Regex`, and entries are removed when the issue is fixed.

**Config defaults are snapshot-tracked** (`docs/config-defaults.md`): every file a fresh server writes into `ModConfig` is snapshotted per pack version into `mods-src/seraphhorizons/assets/seraphhorizons/config/configdefaults/<version>/`, and the pack's mod moves settings still at an old default to the new one at start (`FollowPackDefaults`). So anything that changes a default (a lock change, a default in a mods-src mod's config class, a pack value in `pack/config/ModConfig/`, which ship through this and no longer through Cairn's `modConfig`) needs the snapshot regenerated: `VINTAGE_STORY=... python3 tools/packtool.py smoke --config-defaults write`; CI's smoke job fails otherwise. Ignores, owners and key renames are in `pack/config-defaults.toml`. A released version's snapshot is frozen.

**Recipe browser data flow** (spans C#, Python and TS):
1. `tools/recipe-export/` is a CI-only server mod. `packtool smoke --export` builds it against `$VINTAGE_STORY`, stages it in the smoke run's own `Mods` (never `build/mods`, so it never reaches the lock or `assemble`), and it writes `recipes.json`. Every smoke run also builds `mods-src/seraphhorizons` and stages it the same way, in place of any pinned copy, so the export (and the item-values check on it) and the log scan see the pack's own mod, its items and its schematic gates. Atlas scenarios reference it as a library and call `Exporter.Build` directly.
2. The export format is `schema/recipe-export.schema.json` (with `schema/examples/minimal.json`). `tools/site-data` validates it plus cross-reference rules the schema can't express (`src/checks.ts`), and migrates older exports forward.
3. Releases attach the export; `pages.yml` runs `site-data assemble` over every release's export, then `site/scripts/prepare-data.ts` chunks each version into `site/public/data/<version>/` (`meta.json`, `search.json`, `items/<n>.json`, `recipes/<n>.json`).
4. `site/` is a hash-routed SPA (`#/<version>/item/<code>`) with a relative base; logic lives in `site/src/lib/`, per-shape recipe rendering in `site/src/renderers/`.

**Changing the export format**: a change old readers can't ignore bumps `schemaVersion`. Follow `docs/recipe-browser/deploy.md` exactly: archive the old schema under `schema/archive/`, add a `tools/site-data/src/migrations/vN-to-vN+1.ts` step, and update the exporter (`Exporter.SchemaVersion`), `site/src/lib/export.ts`, `schema.md` and `minimal.json`. A test enforces that every past version has an archived schema and a migration step.

**Pack-authored mods.** `mods-src/<modid>/` holds mods written for this pack: Harmony workarounds for game bugs (`allowedvariantsfix`, recipe filters; `constructionhelpfix`, construction hint lag; `chiselrotationfix`, chiseled blocks in rotated structures), and `seraphhorizons`, the pack's own mod (named after the pack, whose ModDB page it is), holding its tweaks, each switchable in `ModConfig/seraphhorizons.json`: gameplay changes to other mods, an admin `/clear` command (clear weather and daytime, held with `/clear stay`), Tidy Variants, which hides and groups variants in creative and the handbook (#252), and a pack version check (`PackCheck/`: each side compares its loaded mods and game version with `pack/lock.json`, embedded in the DLL, warns in the server log and to admins in chat, and shows a client a dialog). It also adds a machine, the bucking sawmill (`BuckingSawmill/`), a mechanically powered saw built from Immersive Woodworking's sawmill parts that cuts Logging Expanded trunks into logs; its footprint is data in `assets/seraphhorizons/config/buckingmill-rig.json`, and its model includes parts of Immersive Woodworking's sawmill model (gears, blades, saw heads, cranks), which are Bobrik00's, used with permission and outside the repository's license (`CREDITS.md`). A second machine, the rosser (`Rosser/`), a ring debarker generated the same way, strips the branches and bark off those trunks before the mill and hands them on; its output is Logging Expanded's own trunk with a third `branches` state, `debarked`, added by a JSON patch (`patches/rosser-debarkedtrunk.json`, `Rosser/DebarkedTrunks.cs`), and its model also takes parts of Immersive Woodworking's model (`CREDITS.md`). A third, the draw bench (`DrawBench/`, switch `DrawBench`), generated the same way on the shared `Machines/` code, draws a lead or copper hollow section (the game's chute section) into four pipe sections (`seraphhorizons:pipesection-{metal}`) in that metal, its die deciding the metal (iron: lead; steel: lead and copper); its export record is hand-written like the gear cutter's (`tools/recipe-export/Recipes/DrawBenchExport.cs`). A fourth, the press brake (`PressBrake/`, switch `PressBrake`), made the same way but worked by hand (right-click held, as on the quern: no power, no oil), folds a lead or copper plate into two open chute sections; its export record is hand-written too (`PressBrakeExport.cs`, power `hand`). A fifth, the mandrel forging station (`MandrelStation/`, switch `MandrelStation`), a hand station too, hammers a lead or copper hollow (chute) section over a mandrel into two pipe sections, each right-click with a hammer a blow as on the anvil; its export record is `MandrelStationExport.cs` (power `hand`, `turns` the blows). Trunk entities (`TrunkEntities/`) turn Logging Expanded's trunks into entities lying in the world, never items in an inventory: dragged by hand or roped, worked with tools where they lie, or shouldered through Carry On (found by name) onto a station, a rack, a cart or the two machines, which also take a trunk lying in their infeed cells; trunk multiblocks already placed are deleted as they load. What the two machines share is in `Machines/`: the rig maths, footprint, trunk path and Logging Expanded bridge in C#, and the generator package `Machines/tools/machinegen/`; the driver maths exists in Python, C# and the site's TypeScript, held together by `tests/Machines/driver-fixture.json`. Unified pipes (`Pipes/`, `UnifiedPipes`) makes ppex's pipe network the only one: copper and lead states on its pipes and bronze on its valves by JSON patch (`patches/unifiedpipes-ppex.json`, checked against ppex's assets before the patch loader by `Pipes/Core/PipeAssetGuard.cs`), each metal's burst figure from the config and lead bursting on steam or exhaust by Harmony on ppex and exlib (found by name), and its own pipe recipes in place of ppex's, made from its own pipe section (`seraphhorizons:pipesection-*`, one to a straight pipe) along a chain held in `Pipes/Core/PipeSections.cs`: an angle (forged or press-braked), two soldered into the game's chute section (the hollow section, patched to lead and its anvil and plate recipes switched off by `patches/unifiedpipes-chutesection.json`, guarded by `Pipes/Core/ChuteSections.cs`), worked into pipe sections by the machines; iron and steel pipe sections are cast from smex's canal in a `pipe` tool type patched onto smex's own tool mold (`CastPipes`, `patches/castpipes-smexmold.json`, guarded by `Pipes/Core/CastPipeMold.cs`) and banded into pipe on the grid. The other three are separate mods: a `<modid>-v<version>` tag on main makes `mod-release.yml` build one and attach the zip to a GitHub Release (never "latest"); it is uploaded to the ModDB by hand and pinned in `pack.toml` like any other mod. `seraphhorizons` is not: the pack and it are one thing with one version (`pack.toml`'s `version` equals its `modinfo.json`'s and csproj's, `tools/tests/test_mods_src.py`), released together, and it is never pinned in `pack.toml`. `release.yml` builds it on the pack's `v<version>` tag and `packtool assemble --url-mod` puts it in the release's Cairn pack by address (that release's own `seraphhorizons_<version>.zip` asset) and sha256, and in its server bundle as a file; the mod list stays ModDB-only. Releases have no ModDB meta-mod: it could not carry the pack's own mod, and the game reads its dependencies as minimum versions. Uploading the zip to the ModDB is an optional after-step for discoverability (keep the file name), never a gate on the release. The rolling `next` build does the same per commit: CI's cairn job builds `seraphhorizons` and adds it to that run's Cairn pack by address (the `next` release itself, as `seraphhorizons_next_<sha7>.zip`: Cairn re-downloads a followed pack's mod only when its address or version changes, never its hash), and `next.yml` publishes the zip there along with the pack. Every file on `next` says `next` where a versioned release's says the version (`packtool assemble --label next`: `seraphhorizons_next.cairn`, `_server.zip`, `_modlist.txt`, `_recipes.json`); their contents keep the real version. `tests/PackTests` loads the local build as an `<AtlasMod>` and drops any zip of the same modid from `build/mods`; for the other mods-src mods `test_mods_src.py` keeps the pin from getting ahead of the source (it may lag while an upload is pending). Release steps are in each mod's `README.md`. Real logic stays game-independent in a `Core/` folder with an xunit project in the mod's `tests/` that compiles those files directly (`seraphhorizons/TidyVariants/Core`, like `tools/icon-export`); the mod's csproj excludes `tests/**`, and ships `assets/**` in its zip. Only one csproj may sit at a mod's top level (`test_mods_src.py`). Not `mods/`: `packtool fetch` stages `mods/*/` folders as-is.

**Icons.** Icons are rendered by hand in the game client with `tools/icon-export/` (a local mod that must never enter `pack.toml` or a release; a test checks this), then imported by `tools/icons.py import` into content-addressed `icons/<xx>/<sha256>.png` (Git LFS) plus `icons/index.json`. CI checks out without LFS, so it only sees pointer files. See `docs/recipe-browser/icons.md`.

## Troubleshooting crash logs

When diagnosing a crash or error log, search the existing issues (open and closed) for the mods, exception text and symptoms involved before settling on a cause: `gh issue list --state all --search "<terms>"`. A matching or related issue may already hold the cause, a workaround, or evidence that correlates with the new report. Link what you find, and add new evidence to an existing issue instead of opening a duplicate.

## CI and merging

- `ci-ok` in `.github/workflows/ci.yml` is the only required check. **A new CI job must be added to `ci-ok`'s `needs:`**, and to its `MEMOISED` list if the green-tree memo may skip it.
- Green-tree memo: `ci/code-tree.sh` hashes the tree minus no-op paths (docs, `*.md`, `.claude/`, `.mergify.yml`, ...). A passing PR run records `refs/green-trees/<id>`, and later runs on the same id skip the expensive jobs. If you add a path that CI doesn't read, widen the `noop` pattern there deliberately. Anything that is read must stay out of it.
- PRs land only through the Mergify queue: comment `@mergifyio queue` on the PR (not the label), right after opening it. The merge button is blocked by a ruleset. Stacked PRs use `Depends-On: #<n>`. Details are in `docs/merge-queue.md`.
- `release.yml` fires on a `v*` tag matching `pack.toml`'s `version` (and `mods-src/seraphhorizons`' `modinfo.json`), builds the pack's own mod, and publishes it with the pack in one release whose `.cairn` fetches the mod from that release (a jq step checks the address and sha256). `next.yml` republishes the rolling `next` pre-release from main's CI artifacts: the pack's dist and the pack's own mod zip its Cairn pack fetches by hash, in one `gh release create`, only ever forward (the `next` tag must be an ancestor). The zip is named after the commit, and only the current build's zip is on the release (a follower still holding the previous pack document gets the new one when Cairn syncs, on launch). The mod's former rolling release, `seraphhorizons-next`, is retired: `next.yml` deletes it (release and tag) if it still exists. CI's cairn job installs that pack with the zip served on loopback, since the real address still holds the previous build.
- Actions are pinned by commit SHA with a version comment; keep that style.
