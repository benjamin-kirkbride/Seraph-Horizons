# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A version-pinned Vintage Story modpack (game 1.22.7, .NET 10) plus a static **recipe browser** site built from data the pack's own server dumps. `README.md` covers installing, layout and CI; `docs/recipe-browser/` and `docs/merge-queue.md` go deeper. Read the relevant doc before changing a subsystem: they record decisions and engine facts not obvious from the code.

## Commands

Server-dependent work (smoke, Atlas, building the C# mods) needs the .NET 10 SDK and a game server pointed to by `VINTAGE_STORY`: an extracted `vs_server_linux-x64_<ver>` archive, or a full client install of the same version.

```sh
# Pack (tools/packtool.py is stdlib-only Python 3.11+)
python3 tools/packtool.py lock        # after editing pack/pack.toml: resolve pins, rewrite pack/lock.json
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

**Pack.** `pack/pack.toml` is the only hand-edited source of what's in the pack (exact pins, license, `redistribute`, and a `why` per mod). `packtool.py lock` derives `pack/lock.json` (URL + sha256) from it; never hand-edit the lock. Mod files are never re-hosted; artifacts point at the ModDB CDN. `pack/known-errors.json` lists tolerated cross-mod log errors and unresolved BetterRuins schematic blocks for both `smoke` and Atlas; every entry must link an issue, patterns must be valid in both Python `re` and .NET `Regex`, and entries are removed when the issue is fixed.

**Recipe browser data flow** (spans C#, Python and TS):
1. `tools/recipe-export/` is a CI-only server mod. `packtool smoke --export` builds it against `$VINTAGE_STORY`, stages it in the smoke run's own `Mods` (never `build/mods`, so it never reaches the lock or `assemble`), and it writes `recipes.json`. Atlas scenarios reference it as a library and call `Exporter.Build` directly.
2. The export format is `schema/recipe-export.schema.json` (with `schema/examples/minimal.json`). `tools/site-data` validates it plus cross-reference rules the schema can't express (`src/checks.ts`), and migrates older exports forward.
3. Releases attach the export; `pages.yml` runs `site-data assemble` over every release's export, then `site/scripts/prepare-data.ts` chunks each version into `site/public/data/<version>/` (`meta.json`, `search.json`, `items/<n>.json`, `recipes/<n>.json`).
4. `site/` is a hash-routed SPA (`#/<version>/item/<code>`) with a relative base; logic lives in `site/src/lib/`, per-shape recipe rendering in `site/src/renderers/`.

**Changing the export format**: a change old readers can't ignore bumps `schemaVersion`. Follow `docs/recipe-browser/deploy.md` exactly: archive the old schema under `schema/archive/`, add a `tools/site-data/src/migrations/vN-to-vN+1.ts` step, and update the exporter (`Exporter.SchemaVersion`), `site/src/lib/export.ts`, `schema.md` and `minimal.json`. A test enforces that every past version has an archived schema and a migration step.

**Pack-authored mods.** `mods-src/<modid>/` holds mods written for this pack: Harmony workarounds for game bugs (`allowedvariantsfix`, recipe filters; `constructionhelpfix`, construction hint lag; `chiselrotationfix`, chiseled blocks in rotated structures), and `seraphhorizons`, the pack's own mod (named after the pack, whose ModDB page it is; the release meta-mod's modid is `seraphhorizonspack`), holding its tweaks, each switchable in `ModConfig/seraphhorizons.json`: gameplay changes to other mods, an admin `/clear` command (clear weather and daytime, held with `/clear stay`), and Tidy Variants, which hides and groups variants in creative and the handbook (#252). `buckingsawmill` adds a machine, a mechanically powered saw built from Immersive Woodworking's sawmill parts that cuts Logging Expanded trunks into logs; its footprint is data in `assets/buckingsawmill/config/rig.json`, and its model is Bobrik00's, used with permission and outside the repository's license (`CREDITS.md`). A `<modid>-v<version>` tag on main makes `mod-release.yml` build one and attach the zip to a GitHub Release (never "latest"); it is uploaded to the ModDB by hand and pinned in `pack.toml` like any other mod, so pack releases still only point at the ModDB. `tests/PackTests` loads the local build as an `<AtlasMod>` and drops the pinned zip of the same modid from `build/mods`; `tools/tests/test_mods_src.py` keeps the pin from getting ahead of the source (it may lag while an upload is pending). Release steps are in each mod's `README.md`. Real logic stays game-independent in a `Core/` folder with an xunit project in the mod's `tests/` that compiles those files directly (`seraphhorizons/TidyVariants/Core`, like `tools/icon-export`); the mod's csproj excludes `tests/**`, and ships `assets/**` in its zip. Only one csproj may sit at a mod's top level (`test_mods_src.py`). Not `mods/`: `packtool fetch` stages `mods/*/` folders as-is.
**Icons.** Icons are rendered by hand in the game client with `tools/icon-export/` (a local mod that must never enter `pack.toml` or a release; a test checks this), then imported by `tools/icons.py import` into content-addressed `icons/<xx>/<sha256>.png` (Git LFS) plus `icons/index.json`. CI checks out without LFS, so it only sees pointer files. See `docs/recipe-browser/icons.md`.

## CI and merging

- `ci-ok` in `.github/workflows/ci.yml` is the only required check. **A new CI job must be added to `ci-ok`'s `needs:`**, and to its `MEMOISED` list if the green-tree memo may skip it.
- Green-tree memo: `ci/code-tree.sh` hashes the tree minus no-op paths (docs, `*.md`, `.claude/`, `.mergify.yml`, ...). A passing PR run records `refs/green-trees/<id>`, and later runs on the same id skip the expensive jobs. If you add a path that CI doesn't read, widen the `noop` pattern there deliberately. Anything that is read must stay out of it.
- PRs land only through the Mergify queue: comment `@mergifyio queue` on the PR (not the label), right after opening it. The merge button is blocked by a ruleset. Stacked PRs use `Depends-On: #<n>`. Details are in `docs/merge-queue.md`.
- `release.yml` fires on a `v*` tag matching `pack.toml`'s `version`. `next.yml` republishes the rolling `next` pre-release from main's CI artifacts.
- Actions are pinned by commit SHA with a version comment; keep that style.
