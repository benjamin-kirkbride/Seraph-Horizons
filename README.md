# Seraph Horizons

An immersive overhaul modpack for [Vintage Story](https://www.vintagestory.at/), built GTNH-style:
- We take ownership of every included mod and patch for incompatibility and bugs.
- We balance recipes so nothing feels out of place.
- Clear progression.

**Game version:** 1.22.7 (.NET 10) · **Mods:** 118, listed with the reason for each in [`pack/pack.toml`](pack/pack.toml)

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
| `seraphhorizons_<v>_metamod.zip` | Casual singleplayer: drop into `Mods/`, then click "Download mods" on world creation | The game treats these versions as **minimums** |
| `seraphhorizons_<v>_modlist.txt` | `modid@version,...` for launchers that import that format | Exact |

Mod files are never re-hosted here. Everything is fetched from the ModDB CDN, with one exception: the
rolling [`next`](../../releases/tag/next) pre-release's `.cairn` file also installs the pack's own mod
(`mods-src/seraphhorizons`) built from the same commit, which it fetches from the
[`seraphhorizons-next`](../../releases/tag/seraphhorizons-next) pre-release and checks against its sha256.
That zip is named after the commit (`seraphhorizons_<version>_<sha7>.zip`), so its address changes with
every build and Cairn, which notices a changed address or version but not a changed hash, downloads it again.
Versioned releases install it from the ModDB once it is pinned there; `next` keeps installing the commit's build in place of that pin.

## Contributing

The repository layout, the commands for working on the pack, and what CI checks are in
[CONTRIBUTING.md](CONTRIBUTING.md).
