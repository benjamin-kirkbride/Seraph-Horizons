# Seraph Horizons

An immersive overhaul modpack for [Vintage Story](https://www.vintagestory.at/), built GTNH-style:
- We take ownership of every included mod and patch for incompatibility and bugs.
- We balance recipes so nothing feels out of place.
- Clear progression.

<!-- [[[cog
import sys
sys.path.insert(0, "tools")
import packtool
cog.outl(packtool.readme_status())
]]] -->
**Game version:** 1.22.7 (.NET 10) · **Mods:** 127, listed with the reason for each in [`pack/pack.toml`](pack/pack.toml)
<!-- [[[end]]] -->

## Motivation

I have long been disappointed in the lack of cohesive modpacks for VS like Minecraft has long had, a la GT:NH, Skyblock packs, Stoneblock, Crash Landing, etc. so I have decided to change that!

To be clear, this is **NOT** just a "selection of mods" thrown together. It is instead a cohesive overhaul to the game. The intention is that playing this pack should not feel like you have installed a bunch of mods on top of the vanilla game, it should feel like you are playing Vanilla Vintage Story (on hard mode) in the year 2038.

## Principles/Goals of This modpack

- More immersion, less crafting grid (Logging Expanded and Immersive Woodworking, Age of Flax, etc)
- More logistics (Cartwright, Yang's Transport Tycoon, much much rarer ore spawns)
- Harder (harder recipes (not just "more ingredients" or "more steps" either), less hotbar and backpack slots)
- Progression in the form of improving automation as you tech up
- Overhaul of NPC trading, make them a core part of progression and gameplay instead of an afterthought
- Achievements/Quests
- Improved QoL (hotbar refill, player corpse, roll-up bed, many vanilla tweaks, betterer prospecting, visible ores, handbook improvements, many many creative improvements, etc)

## Ore and panning

Ore is rare and placed by the world seed. Each metal has at most one deposit per 5 km cell, so the
nearest deposit of a metal is typically about 2.5 km away. It holds a few hundred ingots (copper and
iron about 400, alloy metals about 150) and runs out. Surface copper and tin are gone. The first metal
comes from panning: one rich gravel field per 1.5 km cell, 300–600 blocks by water or on low ground,
whose pan drops follow the local rock. It also comes from traders, who sell maps to deposits and
gravel fields. Gold, silver and most chromite and platinum come from Interesting Ore Gen's
hydrothermal districts, about one per 120 km². All of this applies to new worlds only. How it works,
the measured numbers and how to re-check them are in [`docs/oregen.md`](docs/oregen.md).

## What I Could Use Help With

- Playtesters: help me make sure the mods interact correctly with eachother, test my tweaks and changes, tell me if recipes seem too easy or hard, etc
- Builders: help me create custom buildings/environments for the purposes of custom pack quests/story lines
- Writers: help me come up with lore for the modpack, and write NPC scripts

## Status

Pre-alpha state. Assume no compatibility for world saves between versions at this stage.

## Installing

Each [GitHub Release](../../releases) has these files:

| File | For | Pinning |
|---|---|---|
| `seraphhorizons_<v>.cairn` | Players using [Cairn](https://mods.vintagestory.at/cairn) (open it in the launcher) and servers using `cairn-server install <file>` | Exact versions, sha256-verified; Cairn installs the matching game and .NET |
| `seraphhorizons_<v>_server.zip` | Plain dedicated servers | `lock.json` + `fetch-mods.sh`, which downloads each mod from the ModDB and checks its sha256 |
| `seraphhorizons_<v>_modlist.txt` | `modid@version,...` for launchers that import that format | Exact |
| `seraphhorizons_<v>.zip` | The pack's own mod (`mods-src/seraphhorizons`), version `<v>` like the pack | The `.cairn` file installs it from the release and checks its sha256; the server bundle carries it; with the mod list, add it to `Mods/` yourself |

The pack's own mod checks each install against the release it came with: if a mod is missing, at
another version than the pack's, or not in the pack at all, or the game is another version, the
server logs it and tells admins as they join, and the game shows a dialog listing it once you are
in a world. It goes quiet once the install matches that release (or for good with
`PackVersionCheck` off in `ModConfig/seraphhorizons.json`).

There is no ModDB meta-mod: it could not carry the pack's own mod, and the game reads a mod's
dependencies as minimum versions, so it would install neither all of the pack nor the pack's versions.

The pack and its own mod are one thing with one version, released together. Other mods' files are never
re-hosted here: they are fetched from the ModDB CDN. The rolling [`next`](../../releases/tag/next)
pre-release has the same files with `next` in place of the version (`seraphhorizons_next.cairn`,
`seraphhorizons_next_server.zip`, ...), and its `.cairn` file does the same with the pack's own mod
built from the same commit, which it fetches from that same pre-release and checks against its sha256.
That zip is named after the commit (`seraphhorizons_next_<sha7>.zip`), so its address changes with
every build and Cairn, which notices a changed address or version but not a changed hash, downloads
it again.

## Contributing

The repository layout, the commands for working on the pack, and what CI checks are in
[CONTRIBUTING.md](CONTRIBUTING.md).
