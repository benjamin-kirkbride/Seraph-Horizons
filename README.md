# Seraph Horizons

A version-pinned [Vintage Story](https://www.vintagestory.at/) modpack, built GTNH-style:
a human-edited manifest, a checksummed lockfile, and CI that boots a real server with
the exact pack before anything ships.

**Game version:** 1.22.7 (.NET 10) · **Mods:** 118, listed with the reason for each in [`pack/pack.toml`](pack/pack.toml)

## Installing

Each [GitHub Release](../../releases) has these files:

| File | For | Pinning |
|---|---|---|
| `seraphhorizons_<v>.cairn` | Players using [Cairn](https://mods.vintagestory.at/cairn) (open it in the launcher) and servers using `cairn-server install <file>` | Exact versions, sha256-verified; Cairn installs the matching game and .NET |
| `seraphhorizons_<v>_server.zip` | Plain dedicated servers | `lock.json` + `fetch-mods.sh`, which downloads each mod from the ModDB and checks its sha256 |
| `seraphhorizons_<v>_metamod.zip` | Casual singleplayer: drop into `Mods/`, then click "Download mods" on world creation | The game treats these versions as **minimums** |
| `seraphhorizons_<v>_modlist.txt` | `modid@version,...` for launchers that import that format | Exact |

Mod files are never re-hosted here. Everything is fetched from the ModDB CDN, with one exception: the
rolling [`next`](../../releases/tag/next) pre-release's `.cairn` file also installs the pack's own mod
(`mods-src/seraphhorizons`) built from the same commit, which it fetches from the
[`seraphhorizons-next`](../../releases/tag/seraphhorizons-next) pre-release and checks against its sha256.
That zip is named after the commit (`seraphhorizons_<version>_<sha7>.zip`), so its address changes with
every build and Cairn, which notices a changed address or version but not a changed hash, downloads it again.
Versioned releases install it from the ModDB once it is pinned there; `next` keeps installing the commit's build in place of that pin.

Packs exported before Cairn 0.9.10 were `.cairn.json` files, and older releases still carry that name.
The contents are the same and Cairn imports either. Following `next` by its file's address means
following the new name once: import `seraphhorizons_<v>.cairn` from the `next` release again.

## Layout

```
pack/pack.toml          what's in the pack (edit this): game version, mods, licenses, why
pack/lock.json          generated: exact release, file URL, sha256 and ModDB asset id per mod
pack/config/ModConfig/  per-mod config overrides, .json or ConfigKit .yaml (shipped via the .cairn file)
pack/known-errors.json  understood cross-mod errors the tests tolerate, one issue each
tools/packtool.py       lock / check / fetch / smoke / outdated / assemble (stdlib Python 3.11+)
tests/PackTests/        Atlas scenarios: a headless server in `dotnet test` with the whole pack
mods-src/               mods built here, uploaded to the ModDB by hand, then pinned like any other
.github/                CI, release and nightly update-check workflows
```

The [recipe browser](docs/recipe-browser/README.md), a site for looking up the pack's
recipes outside the game, lives in `schema/`, `tools/recipe-export/`, `tools/site-data/`,
`tools/icon-export/`, `tools/icons.py`, `icons/` and `site/`. The same site has a
[model viewer](docs/recipe-browser/models.md) (`#/models`) for the machine models of the
pack's own mods: it draws each shape from `mods-src/`, moves it by its rig, and lets you pick
any element by name. `site/models.json` lists the models.

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
- **atlas**: [Atlas](https://github.com/Pixnop/Atlas) scenarios check that each locked mod loads at its locked version, that boot is clean, and that every block BetterRuins' ~800 schematics actually place resolves, after the engine's legacy remaps. The same job runs the game-independent unit tests of the pack-authored mods' logic (`mods-src/seraphhorizons/tests`).
- **cairn**: builds the pack's own mod (`mods-src/seraphhorizons`), assembles the release with that zip in the Cairn pack the way `next` ships it, and installs the `.cairn` file with the real `cairn-server`. Cairn re-downloads and sha256-verifies every mod, and its lock must match ours. The mod's address is the `seraphhorizons-next` release, under a name carrying the commit (`seraphhorizons_<version>_<sha7>.zip`), and serves nothing until `next.yml` publishes this build there, so the install reads a copy of the pack that fetches the zip from a loopback web server instead; the lock and hash are the ones that ship.
- **tools**: unit tests of the Python tools, `tools/site-data` and the site.
- **export**: the recipe export that the smoke job's server dumped matches the schema, and its cross-references hold. It also warns about items that have no icon.
- **site**: Playwright tests of the recipe browser and the model viewer, built from that same export.

`ci-ok` aggregates these jobs and is the one check branch protection requires. PRs land through the Mergify merge queue: comment `@mergifyio queue` on the PR as soon as it is opened. See [docs/merge-queue.md](docs/merge-queue.md).

`release.yml` runs on a `v*` tag that matches `pack.toml`'s version. It reruns CI and then publishes `dist/` as a GitHub Release. It is marked as the repository's [latest release](../../releases/latest). `next.yml` runs after CI passes on a push to main and republishes the rolling [`next`](../../releases/tag/next) pre-release from the `dist` artifact that CI run tested, whatever the pack version is. Just before it, in the same job, it republishes the rolling [`seraphhorizons-next`](../../releases/tag/seraphhorizons-next) pre-release with the `mods-src/seraphhorizons` zip that run's cairn job built, which `next`'s `.cairn` file fetches; the two always describe the same commit, and the pack is not published if the mod's publish fails. The zip is named after its commit, and the previous build's zip, the one the outgoing `next` pack names, is kept on the release for one more publish so a follower still on that pack can install it. Versioned mod releases are `mod-release.yml`'s, on a `<modid>-v<version>` tag. `update-check.yml` runs nightly and keeps a "Mod updates available" issue current. It fails if a pinned release is retracted. Both releases also carry `seraphhorizons_<v>_recipes.json`, the recipe export; `pages.yml` builds the recipe browser from the export of every release, with the model viewer's models from the commit it builds, and deploys it to GitHub Pages.

Game server binaries are downloaded from Anego's public CDN and cached per version. They are never committed or re-published.
