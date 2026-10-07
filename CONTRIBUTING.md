# Contributing

How the repository is laid out, how to work on the pack, and what CI checks.
Installing the pack is in the [README](README.md).

## Layout

```
pack/pack.toml          what's in the pack (edit this): game version, mods, licenses, why
pack/lock.json          generated: exact release, file URL, sha256 and ModDB asset id per mod
pack/config/ModConfig/  per-mod config overrides, .json or ConfigKit .yaml (shipped via the .cairn file)
pack/known-errors.json  understood cross-mod errors the tests tolerate, one issue each
tools/packtool.py       lock / check / fetch / smoke / outdated / assemble (stdlib Python 3.11+)
tests/PackTests/        Atlas scenarios: a headless server in `dotnet test` with the whole pack
mods-src/               mods built here: the pack's own (seraphhorizons, released with the pack) and fixes pinned from the ModDB
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
uvx --from cogapp==3.6.0 cog -r README.md   # regenerate README's status line (game version, mod count)

# tests (need the .NET 10 SDK and an extracted vs_server_linux-x64_<ver> archive)
export VINTAGE_STORY=~/vs
python3 tools/packtool.py smoke       # build mods-src/seraphhorizons, boot a dedicated server with it on a standard world, scan logs
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

- **lock**: `pack.toml` and `lock.json` agree, every declared mod dependency is in the pack, no locked release has been retracted, and README's generated status line (`cog --check`) matches `pack.toml`.
- **smoke**: boots `VintagestoryServer` with the full pack on a standard world (fixed seed), waits for spawn-chunk worldgen, then runs `/stop`. The pack's own mod is built from `mods-src/seraphhorizons` (Release) and loaded in place of any pinned copy of its modid, staged in the run's own `Mods` only (never `build/mods`, the lock or `assemble`), so the log scan, the export and the item-values check on it see the mod as the tree has it, its items and its schematic gates. It fails on any `[Error]`/`[Fatal]`, failed JSON patch, exception, or a locked mod (or that build) that didn't load, except errors matching an entry in `pack/known-errors.json`. Each entry links the issue that explains it and is removed when that issue is fixed.
- **atlas**: [Atlas](https://github.com/Pixnop/Atlas) scenarios check that each locked mod loads at its locked version, that boot is clean, and that every block BetterRuins' ~800 schematics actually place resolves, after the engine's legacy remaps. The same job runs the game-independent unit tests of the pack-authored mods' logic (`mods-src/seraphhorizons/tests`).
- **cairn**: builds the pack's own mod (`mods-src/seraphhorizons`), assembles the release with that zip in the Cairn pack the way `next` ships it (`packtool assemble --label next`, so the files are `seraphhorizons_next.cairn`, `seraphhorizons_next_server.zip` and `seraphhorizons_next_modlist.txt`), and installs the `.cairn` file with the real `cairn-server`. Cairn re-downloads and sha256-verifies every mod, and its lock must match ours. The mod's address is the `next` release, beside the pack, under a name carrying the commit (`seraphhorizons_next_<sha7>.zip`), and serves nothing until `next.yml` publishes this build there, so the install reads a copy of the pack that fetches the zip from a loopback web server instead; the lock and hash are the ones that ship.
- **tools**: unit tests of the Python tools, `tools/site-data` and the site.
- **export**: the recipe export that the smoke job's server dumped matches the schema, and its cross-references hold. The item values table the pack's mod ships equals a rebuild from that export, and every item its traders buy has a value (`tools/item-values/itemvalues.py check`). It also warns about items that have no icon.
- **site**: Playwright tests of the recipe browser and the model viewer, built from that same export.

`ci-ok` aggregates these jobs and is the one check branch protection requires. PRs land through the Mergify merge queue: comment `@mergifyio queue` on the PR as soon as it is opened. See [docs/merge-queue.md](docs/merge-queue.md).

`release.yml` runs on a `v*` tag that matches `pack.toml`'s version, which is also `mods-src/seraphhorizons`' version. It reruns CI, builds the pack's own mod, assembles `dist/` with that zip in the Cairn pack by the release's own address and sha256 (checked before publishing) and in the server bundle, and publishes the zip and `dist/` as one GitHub Release. It is marked as the repository's [latest release](../../releases/latest). `next.yml` runs after CI passes on a push to main and republishes the rolling [`next`](../../releases/tag/next) pre-release from the `dist` artifact that CI run tested, whatever the pack version is, together with the `mods-src/seraphhorizons` zip that run's cairn job built, which `next`'s `.cairn` file fetches from that same release: one publish, so the pack and the zip it names appear together. The zip is named after its commit, and the previous build's zip, the one the outgoing `next` pack names, is kept on the release for one more publish so a follower still on that pack can install it. (The mod's former rolling release, `seraphhorizons-next`, is retired; `next.yml` deletes it if it still exists.) Versioned releases of the other `mods-src/` mods are `mod-release.yml`'s, on a `<modid>-v<version>` tag; it refuses `seraphhorizons`, which is released with the pack. `update-check.yml` runs nightly and keeps a "Mod updates available" issue current. It fails if a pinned release is retracted. Both releases also carry the recipe export, `seraphhorizons_<v>_recipes.json` (on `next`, `seraphhorizons_next_recipes.json`: every file there says `next` where a versioned release's says the version); `pages.yml` builds the recipe browser from the export of every release, with the model viewer's models from the commit it builds, and deploys it to GitHub Pages.

Game server binaries are downloaded from Anego's public CDN and cached per version. They are never committed or re-published.
