# Ore survey (seraphoresurvey)

Measures where worldgen puts ore: how many deposits of each metal there are per km², how many
ingots each holds, how far apart they are and whether they reach the surface, plus coal, the
industrial minerals and rich gravel. It produced the "What the pack has today" table in the ore
epic (#435), and is how later worldgen changes are checked against the epic's targets.

It is a local tool: the mod is not in `pack/pack.toml`, no release includes it
(`tools/tests/test_packtool_assemble.py` checks this), and no CI job runs it.

```
OreSurvey.cs           the server mod: generates an area, records ore cells, stops the server
Census.cs              its census mode: each deposit measured by a flood over chunk columns
WorldgenPasses.cs      lists, times (--profile) or drops (--fast) the worldgen handlers
Core/                  game-independent logic (the flood, ore names), tested by tests/
run.sh                 runs the mod for several seeds in parallel, pack or vanilla
ore_survey.py          summary / compare / check / census (stdlib Python)
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
| `ORE_SURVEY_MODE` | `dump` for the dump, `census` for a census (below); anything else scans |
| `ORE_SURVEY_FAST`, `ORE_SURVEY_PROFILE` | `1`: `--fast`, `--profile` |
| `ORE_CENSUS_METALS`, `ORE_CENSUS_KINDS`, `ORE_CENSUS_PARALLEL` | `--metals`, `--kinds`, `--floods` |

It loads the area in tiles of 8 x 8 chunk columns, counts every block of every chunk, and
unloads each tile before asking for the next, so memory stays flat. When done it stops the
server.

## Census

A full scan generates every column of its window: a 320 x 320 run takes about 2.5 hours and
sees two to five deposits per metal. The census measures deposits one at a time instead, so it
needs only the columns around each, about a tenth of a second of worldgen per deposit column:

```sh
VINTAGE_STORY=$HOME/Games/vintagestory tools/ore-survey/run.sh --dump --name comb   # once per mod set
VINTAGE_STORY=$HOME/Games/vintagestory tools/ore-survey/run.sh --census --fast --name comb \
  --size 640 -j 4 --port 42600 --mod build/seraphhorizons_<version>.zip 101 202 303 404
python3 tools/ore-survey/ore_survey.py census build/ore-survey/comb-s{101,202,303,404} \
  --dump build/ore-survey/comb-dump/orescan.json --json build/ore-survey/comb-census.json --check
```

`--kinds districts` (or `cells`) measures only district veins (or only ore cells): a district
change's census of six 640 squares takes about half an hour on three servers.

What it measures, in the area (`--size` chunk columns square at the map centre, 320 by default):

- **Ore cells.** Every cell of every sized metal (`config/ore-sizes.json` with `smallIngots`,
  managed by the cell rule) that the area touches, or those of `--metals`. The pack's
  `OreCellPlacement` is read by reflection (its spots and cell book), and the cell is followed as
  `DepositService.Verify` follows it: the active spot's column is generated, which places the vein
  or moves the cell to its next spot, until a vein is placed or no spot is left (`none`).
  Fallback spots generated before their turn are skipped by the cell rule, so which fallback a
  cell ends on depends on what was generated first, here as in a played world.
- **District veins.** Interesting Ore Gen's districts are built from the seed before anything
  generates (its own `TrySpawnDistrictsNear`, for every 7 km tile the area touches, so the pack's
  `DistrictVeinSizes` has cut them): every ore shoot and ladder vein still holding a sized metal
  whose box centre lies in the area is one record, per metal, with the district's code and host
  rock, the zone's kind, ores and band of heights. Horsetail lenses are left out. The record's
  `rocks` are the commonest rocks in the band over the 5 x 5 columns around its centre: a mafic
  district's shoot is ore across its whole ellipse only where that is basalt.
- **The flood** (`Core/ColumnFlood.cs`): from the deposit's anchor column, every column within 2
  of a column holding the deposit's metal (any of its ores, prospect veins included) is
  generated and read, until an empty margin of 2 columns surrounds all of it; never further than
  16 columns from the anchor (`capped` when it wanted to). Only that metal's ore is recorded.
- **Columns read early.** A column asked for drags its neighbours through the earlier passes, and
  those within three or so pass Vegetation. By then a column's ore is final (its neighbours have
  finished TerrainFeatures, where all ore is placed), so the census reads every column at its
  Vegetation pass (its own handler there) and asks only for columns not seen yet. Each column
  asked for is read again once complete and compared (`earlyChecked`, `earlyMismatch` in
  census.json; `ORE_CENSUS_EARLY=0` turns this off).

`ore_survey.py census` joins records of one metal that share a cell into one deposit (a vein
reached from two anchors, or a cell's vein run into a district's), counts its ingots from its
fragments of 100 blocks or more as the summary does (smaller ones are pockets), and reports per
metal and per source (cells, districts, both): deposits of 50 ingots or more, `small` ones,
cells with `none`, ingots p10 / median / p90 / max, the surface share, the grade shares, the
share capped and the median columns per deposit, then each district vein with its rocks.
`--check` reads p10 / median / p90 against `targets.json` as `check` does (`--source` picks the
deposits); a census measures sizes, not spacing, so it has no density rows.

Throughput: worldgen runs on one thread per server, so servers in parallel (`-j`) are the
lever; each server floods four deposits at once (`--floods`). Most of a deposit's cost is the
neighbours each complete column needs (out to five columns for Terrain).

### Against a full scan

Seeds 101 and 303, the census and a 320 x 320 scan of the same mods (2026-10-13), each census
deposit matched to the scan's deposit at the same place: district veins agree exactly (18 of 20;
the other two are a 150 m merge and a neighbour), and of 36 cell deposits 9 agree exactly, 21
within 10 % and 15 differ by more (−58 % to +197 %). 40 of 43 cells chose the same spot, and
the census is exactly reproducible (the same blocks over five runs, with columns asked for in
other orders), as is the scan (a 96 and a 320 square agree column for column). So an ore cell's
vein depends on the order its columns generate in, a census's anchor first, a scan's in tiles;
it is not the structures (the same with GenStructures taken out of both). Read cell sizes from a
census as one valid order among others, not as the scan's. District veins do not have this.
Of 8,834 early scans checked against their complete column (eight census runs), 8 differed: a
structure from a column further off reaching in later, at a guess; a deposit's count can be off
by that, rarely.

Why, from `ORE_TRACE_CHUNK=x,z[,r]` (`OreTrace.cs`: read-only Harmony postfixes that log the
cell rule's decision on every try from chunk x,z, or with r for every column within r of it,
to `<out>.trace`), copper cell 102,102 of seed 101: both orders approve the same try in every
column. But Interesting Ore Gen's `TryPlaceMainOre` writes to any chunk the worldgen accessor can
reach, not only the one being generated, and draws from the vein's `DepositRand` (the surface
indicator's chance, and `TryPlaceBlockForWorldGen`) only where the block it finds is reachable
host rock; `GenerateMultiOreFaultPocket` returns early unless its centre's chunk is reachable
and host rock. So how many draws a pass makes depends on what earlier passes already placed or
loaded, every later draw shifts, and each column gets a differently shaped vein: the scan's
copper reaches y 72–80 in a column where the census's stops at 64. The pack's cell rule adds
its own: a try whose centre chunk is not reachable approves nothing in that pass (12 of the
scan's 49 columns around the anchor). District veins are sampled per block from fixed shapes, so
they come out alike.

### Fast

`--fast` (for a scan too) takes the handlers of the passes after TerrainFeatures out of the
standard world type: Vegetation (light, rivulets, vegetation, story structures),
NeighbourSunLightFlood (light, snow) and PreDone (creatures, tree hollows). Ore is placed in
Terrain and TerrainFeatures (GenDeposits, IOG's districts, the pack's placer fields, after
structures, ponds and dungeons, which stay), and the passes still run, empty, so columns complete
as before. A census of seed 101 gave the same ore cell for cell with and without it, about a third
faster. `--profile` logs each handler's time at the end; `ORE_SURVEY_SKIP` takes out handlers by
name (an experiment, e.g. `GenStructures`).

### Census output

`census.json`: `seed`, `area`, `areaKm2`, `seconds`, column counts, and `deposits`, one record
per cell or district vein: `id`, `kind` (`cell` or `district`), `metal`, `status` (`flooded`,
`none`: the cell's spots all failed, `unresolved`), `anchorX`/`anchorZ` (blocks), for a cell
`cellX`, `cellZ`, `spot`, for a district vein `district` (`code`, `host`, `centreX`, `centreZ`,
`zone`, `zoneKind`, `ores`, `box`), `bandY1`/`bandY2` and `rocks`; then `columns` (flooded),
`oreColumns`, `capped`, `bbox` (blocks, from 8-block cells), `blocks`, `generated` (columns it
asked for) and `seconds`. `census.json.cells.csv`: the record's id, then the scan's six columns,
with cell coordinates absolute (block / 8). Every server stops autosaving (the world is thrown
away): under a census's load the autosave could not pause the server and once crashed its chunk
database thread.

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
  Grade within deposits (#944): the median deposit's share of its blocks in its commonest grade
  (`top grade`), and the share of deposits with 80 % or more in one grade (`1 grade`), from the
  grade in each block's code.
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
