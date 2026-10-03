# Seraph Horizons

A version-pinned [Vintage Story](https://www.vintagestory.at/) modpack, built GTNH-style:
a human-edited manifest, a checksummed lockfile, and CI that boots a real server with
the exact pack before anything ships.

**Game version:** 1.22.7 (.NET 10) · **Mods:** 118, listed with the reason for each in [`pack/pack.toml`](pack/pack.toml)

## Installing

Each [GitHub Release](../../releases) has these files:

| File | For | Pinning |
|---|---|---|
| `seraphhorizons_<v>.cairn.json` | Players using [Cairn](https://mods.vintagestory.at/cairn) (open it in the launcher) and servers using `cairn-server install <file>` | Exact versions, sha256-verified; Cairn installs the matching game and .NET |
| `seraphhorizons_<v>_server.zip` | Plain dedicated servers | `lock.json` + `fetch-mods.sh`, which downloads each mod from the ModDB and checks its sha256 |
| `seraphhorizons_<v>_metamod.zip` | Casual singleplayer: drop into `Mods/`, then click "Download mods" on world creation | The game treats these versions as **minimums** |
| `seraphhorizons_<v>_modlist.txt` | `modid@version,...` for launchers that import that format | Exact |

Mod files are never re-hosted here. Everything is fetched from the ModDB CDN.

## Layout

```
pack/pack.toml          what's in the pack (edit this): game version, mods, licenses, why
pack/lock.json          generated: exact release, file URL, sha256 per mod
pack/config/ModConfig/  per-mod config overrides, .json or ConfigKit .yaml (shipped via the .cairn.json file)
pack/known-errors.json  understood cross-mod errors the tests tolerate, one issue each
tools/packtool.py       lock / check / fetch / smoke / outdated / assemble (stdlib Python 3.11+)
tests/PackTests/        Atlas scenarios: a headless server in `dotnet test` with the whole pack
mods-src/               mods built here, uploaded to the ModDB by hand, then pinned like any other
.github/                CI, release and nightly update-check workflows
```

The [recipe browser](docs/recipe-browser/README.md), a site for looking up the pack's
recipes outside the game, lives in `schema/`, `tools/recipe-export/`, `tools/site-data/`,
`tools/icon-export/`, `tools/icons.py`, `icons/` and `site/`.

## Working on the pack

```sh
# after editing pack/pack.toml
python3 tools/packtool.py lock        # resolve pins via the ModDB API, write pack/lock.json
python3 tools/packtool.py fetch       # download + verify into build/mods

# tests (need the .NET 10 SDK and an extracted vs_server_linux-x64_<ver> archive)
export VINTAGE_STORY=~/vs
python3 tools/packtool.py smoke       # boot a dedicated server on a standard world, scan logs
dotnet test tests/PackTests           # Atlas scenarios

python3 tools/packtool.py outdated    # newer compatible releases / retractions
python3 tools/packtool.py assemble    # build dist/ (what a release publishes)
```

Atlas writes a ~750 MB scratch world per test class under `$TMPDIR/atlas` (`/tmp/atlas` by
default). It deletes a passing class's world, keeps a failing one for its `Logs/`, and
leaves the run's last world (Atlas 0.15.0 never disposes that host) and anything an
interrupted run had; `tests/PackTests` deletes worlds idle for over two hours when it
starts (`ATLAS_KEEP_SCRATCH=1` keeps them all). If `/tmp` is a tmpfs, those worlds sit in
RAM: point the tests at disk with `export TMPDIR=~/.cache/atlas-tmp` (create it
first). An IDE test runner needs the variable in its own settings.

## CI

`ci.yml` runs on every PR, fork PRs included. It uses GitHub-hosted runners only and needs no secrets.

- **lock**: `pack.toml` and `lock.json` agree, every declared mod dependency is in the pack, and no locked release has been retracted.
- **smoke**: boots `VintagestoryServer` with the full pack on a standard world (fixed seed), waits for spawn-chunk worldgen, then runs `/stop`. It fails on any `[Error]`/`[Fatal]`, failed JSON patch, exception, or a locked mod that didn't load, except errors matching an entry in `pack/known-errors.json`. Each entry links the issue that explains it and is removed when that issue is fixed.
- **atlas**: [Atlas](https://github.com/Pixnop/Atlas) scenarios check that each locked mod loads at its locked version, that boot is clean, and that every block BetterRuins' ~800 schematics actually place resolves, after the engine's legacy remaps. The same job runs the game-independent unit tests of the pack-authored mods' logic (`mods-src/seraphtweaks/tests`).
- **cairn**: assembles the release and installs the `.cairn.json` file with the real `cairn-server`. Cairn re-downloads and sha256-verifies every mod, and its lock must match ours.
- **tools**: unit tests of the Python tools, `tools/site-data` and the site.
- **export**: the recipe export that the smoke job's server dumped matches the schema, and its cross-references hold. It also warns about items that have no icon.
- **site**: Playwright tests of the recipe browser, built from that same export.

`ci-ok` aggregates these jobs and is the one check branch protection requires. PRs land through the Mergify merge queue: comment `@mergifyio queue` on the PR as soon as it is opened. See [docs/merge-queue.md](docs/merge-queue.md).

`release.yml` runs on a `v*` tag that matches `pack.toml`'s version. It reruns CI and then publishes `dist/` as a GitHub Release. It is marked as the repository's [latest release](../../releases/latest). `next.yml` runs after CI passes on a push to main and republishes the rolling [`next`](../../releases/tag/next) pre-release from the `dist` artifact that CI run tested, whatever the pack version is. `update-check.yml` runs nightly and keeps a "Mod updates available" issue current. It fails if a pinned release is retracted. Both releases also carry `seraphhorizons_<v>_recipes.json`, the recipe export; `pages.yml` builds the recipe browser from the export of every release and deploys it to GitHub Pages.

Game server binaries are downloaded from Anego's public CDN and cached per version. They are never committed or re-published.
