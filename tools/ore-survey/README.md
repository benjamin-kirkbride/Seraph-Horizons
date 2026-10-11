# Ore survey (seraphoresurvey)

Measures where worldgen puts ore: how many deposits of each metal there are per km², how many
ingots each holds, how far apart they are and whether they reach the surface, plus coal, the
industrial minerals and rich gravel. It produced the "What the pack has today" table in the ore
epic (#435), and is how later worldgen changes are checked against the epic's targets.

It is a local tool: the mod is not in `pack/pack.toml`, no release includes it
(`tools/tests/test_packtool_assemble.py` checks this), and no CI job runs it.

```
OreSurvey.cs           the server mod: generates an area, records ore cells, stops the server
run.sh                 runs the mod for several seeds in parallel, pack or vanilla
ore_survey.py          summary / compare / check (stdlib Python)
targets.json           the epic's targets, for check
```

## Build

```sh
VINTAGE_STORY=$HOME/Games/vintagestory dotnet build tools/ore-survey -c Release
```

This writes `build/seraphoresurvey.zip`, which `run.sh` copies into each run's `Mods`.

## Run

`run.sh` needs `VINTAGE_STORY` (a 1.22.7 server or client install) and, for the pack, the
pinned mods in `build/mods` (`python3 tools/packtool.py fetch`).

```sh
# The dump: deposit definitions, ore drops and metal units. ore_survey.py needs it to turn
# blocks into ingots. Takes a minute; redo it when the mods change.
VINTAGE_STORY=$HOME/Games/vintagestory tools/ore-survey/run.sh --dump

# Five seeds, 160 x 160 chunks (5.1 km square, 26.2 km² each), four at a time.
VINTAGE_STORY=$HOME/Games/vintagestory tools/ore-survey/run.sh 101 202 303 404 505

python3 tools/ore-survey/ore_survey.py summary build/ore-survey/pack-s{101,202,303,404,505} \
  --json build/ore-survey/pack.json
```

Options (`run.sh --help`): `--size N` chunk columns per side (default 160, a multiple of 8);
`--vanilla` runs without the pack's mods (output under `vanilla-s<seed>`); `--mods DIR` takes
them from elsewhere; `--mod ZIP` adds a mod and drops any staged zip of the same modid, for a
local build of `seraphhorizons` with a worldgen change; `--modconfig DIR` copies a `ModConfig`
folder in; `--name NAME` sets the output prefix; `-j N` (default 4) and `--port N` (default
42460, run k uses N+k).

A 160 x 160 run takes about 35 minutes with the full pack; the epic's runs went four at a
time. Each run's data path is `build/ore-survey/<name>-s<seed>/`; the
world, `Mods` and `Cache` are deleted when it ends, the logs (`server.out`, `Logs/`) are kept.

The mod reads its settings from the environment, which `run.sh` sets:

| Variable | Meaning |
|---|---|
| `ORE_SURVEY_OUT` | output file (default `orescan.json` in the server's working directory) |
| `ORE_SURVEY_SIZE` | chunk columns per side, centred on the map centre (default 64) |
| `ORE_SURVEY_MODE` | `dump` for the dump; anything else scans |

It loads the area in tiles of 8 x 8 chunk columns, counts every block of every chunk, and
unloads each tile before asking for the next, so memory stays flat. When done it stops the
server.

## Before and after a worldgen change

```sh
VINTAGE_STORY=$HOME/Games/vintagestory dotnet build mods-src/seraphhorizons -c Release
VINTAGE_STORY=$HOME/Games/vintagestory tools/ore-survey/run.sh --name after \
  --mod build/seraphhorizons_<version>.zip 101 202 303 404 505
python3 tools/ore-survey/ore_survey.py summary build/ore-survey/after-s{101,202,303,404,505} \
  --dump build/ore-survey/pack-dump/orescan.json --json build/ore-survey/after.json
python3 tools/ore-survey/ore_survey.py compare build/ore-survey/pack.json build/ore-survey/after.json
python3 tools/ore-survey/ore_survey.py check build/ore-survey/after.json   # exits 1 on a miss
```

The default dump is the run folder's sibling with `-s<seed>` replaced by `-dump`
(`after-dump`); pass `--dump` to use another one, or run `run.sh --dump --name after` when the
change touches drops or metal units.

## How the summary counts

- **Cells.** The mod records ore and rich gravel by 8 x 8 x 8 block cell: the block count and
  the shallowest depth below the worldgen surface (`WorldGenTerrainHeightMap`) of any of them.
- **Fragments.** Per ore type (nativecopper, malachite, ...), cells that touch, including on
  edges and corners (26 neighbours), are one fragment. Fragments under 100 blocks
  (`--min-blocks`) are **pockets**, reported on their own (count per km², median ingots, share
  at the surface, share of the metal).
- **Deposits.** Fragments of one metal, of any ore type, whose centres are closer than 150 m
  (`--link`) are joined, transitively; the deposit sits at its largest fragment's centre.
  Deposits under 50 ingots (`--min-ingots`) are not counted; the epic's table used 50. When
  the targets shrink deposits below that (silver and gold aim at 30 small), pass a lower value
  to both summaries of a comparison.
- **Ingots.** From the dump: an ore block drops 1.25 ore chunks and 0.01 crystallised ore;
  each has `metalUnits` by grade; 5 units are a nugget and 20 nuggets an ingot. The metal is
  what the ore's nugget smelts to (iron ores as iron). Argentiferous galena (`galena_nativesilver`)
  counts as lead since #690 (its silver is won by cupellation); the epic's surveys, and
  `config/ore-sizes.json`'s medians, counted it as silver.
- **Per metal:** deposits per km² (and km² per deposit), ingots p10 / median / p90 / max (no
  interpolation: the value at index `int(p * n)`), the median distance from a deposit to the
  nearest other deposit of the same metal in the same seed, the share of deposits with ore
  within 6 blocks of the surface, and all ore of the metal (pockets included) in ingots per km².
- **Coal and industrial minerals** (coal, sulfur, saltpeter, borax, cinnabar, alum, manganese
  from rhodochrosite) are grouped the same way and counted in blocks; deposits under 1,000
  blocks (`--min-mineral-blocks`) are not counted. Gems, quartz, olivine and flint are left out.
- **Rich gravel** (`richgravel-*`, any rock): cells whose centres are within 16 m horizontally
  (`--gravel-link`) are one field; fields under 100 blocks (`--min-field-blocks`) count as
  scattered gravel. Reported: fields per km², blocks per field p10 / median / p90, nearest
  field, share at the surface, all rich gravel and scattered rich gravel per km².

`check` reads the targets as: each metal's p10 / median / p90 against its small / typical /
large size, and its deposits per 25 km² against 1, each within a factor of 1.5
(`tolerance`); gravel fields per 2.25 km² (a 1.5 km cell) against 1 within the same factor, and
the median field between 300 and 600 blocks. `compare` prints two summaries side by side with
ratios, then checks the second.

## Output formats

`run.sh` writes, per run:

- `orescan.json`: `seed`, `seaLevel`, `mapSizeY`, `chunkColumns`, `areaKm2`, `seconds`, and
  `blocks`: every block code found, with `count`, `meanY` and `cls` (block class).
- `orescan.json.cells.csv`: no header; one line per (block code, cell):
  `code,cellX,cellZ,cellY,blocks,minDepth`. Cell coordinates are block / 8, X and Z from the
  area's north-west corner, Y from the bottom of the world. `minDepth` is the worldgen surface
  height minus the block's Y; it is negative for ore above the surface (in a cliff face).
  Recorded codes: `ore-*`, anything of class `BlockOre`, Interesting Ore Gen's `saltpeterore`,
  `looseores-*` (surface signs; the summary ignores them) and `richgravel-*`.
- `orescan.json.log`: progress, one line per row of tiles.

The dump's `orescan.json` has `blocks` (each ore block's drops: `code`, `avg`, `last`),
`items` (each ore, crystallised ore and nugget: `cls`, `metalUnits`, `smeltsTo`,
`smeltsToSize`, `smeltedRatio`) and `deposits` (every `GenDeposits` deposit variant, with
`TriesPerChunk`, generator, ore map settings, attributes and child deposits).

`summary --json` writes the numbers it prints, with the runs, the options and the dump used.

## How the epic's table was produced

The table in #435 ("What the pack has today") came from the scratch version of this tool,
which this one replaces with the same cell format: the full pack (Interesting Ore Gen 2.3.8) at
the pins of 2026-10-04, seeds 101, 202, 303, 404 and 505, 160 x 160 chunks each (131 km² in
all), four runs in parallel at about 35 minutes each, and the summary with the defaults above.
Its results are in the main checkout's `build/orescan-pack-s{101,202,303,404,505}/` and
`build/orescan-pack-dump/` and read as they are:

```sh
python3 tools/ore-survey/ore_survey.py summary build/orescan-pack-s{101,202,303,404,505}
```

This reproduces the table, apart from chromium's p10 (62 here and in the scratch script's own
output; the table says 74). Those runs did not record rich gravel; their gravel section is
empty. The scratch script also valued manganese (rhodochrosite) in ingots; it is counted in
blocks here.
