# Ore generation, deposits and maps

The ore overhaul (epic #435), built in the pack's own mod (`mods-src/seraphhorizons/Ore/`). Ore is
rare and placed by the seed; a deposit lasts a group a long while and then runs out; the first
metal comes from rich gravel fields and from traders, who sell maps to both. This note covers what
the code does and the facts it rests on; the switches and commands are in the mod's README
("Ore cells", "Placer fields, deposits and ore maps"), the admin tools in `docs/admin-tools.md`. Game 1.22.7, Interesting Ore Gen 2.3.8,
Wilderlands Panning 1.0.9.

Every change to generation applies to **new worlds only**: the world records at its first start
which switches it was created with (`seraphhorizons:oreworld`, `Ore/Core/OreWorldRecord.cs`). A key
missing from an older record reads as off, so a world created before a switch existed never gets it.

## Targets (epic #435)

A typical deposit of a metal about 2.5 km away (one per metal per 5 km cell), lasting a group of
about four through an age; the first metal from a rich gravel field about 750 m away (one per
1.5 km cell) and from traders.

| Metal | Small | Typical | Large (ingots) |
|---|---|---|---|
| Copper, iron | 150 | 400 | 1,000 |
| Tin, zinc, bismuth, lead, nickel | 60 | 150 | 400 |
| Silver, gold | 30 | 80 | 200 |
| Titanium, chromium, platinum | 60 | 150 | 400 |

Gravel fields hold 300–600 rich gravel blocks; scattered rich gravel is cut to a quarter; surface
copper and tin are gone; hydrothermal districts are rarer. `tools/ore-survey/targets.json` holds the
same numbers for `ore_survey.py check`, which reads small / typical / large as p10 / median / p90.

## Ore cells (#438)

- The world is cut into 5 km cells per metal (copper, iron, ..., and coal and each industrial
  mineral as their own "metal"; `Ore/Core/OreMetals.cs`). Each (seed, metal, cell) has eight spots
  from a stable hash (`OreCells`, SplitMix64 over the seed, the metal's FNV-1a and the cell), at
  least a tenth of the cell from its edge. The first is the deposit's, the rest fallbacks.
- A spot is a chunk column, its anchor. Interesting Ore Gen's spacing filter
  (`TiltedDiscDepositGenerator.TryApproveOreSpawnSeed`) is replaced: a vein try is approved only if
  it comes from the active spot's anchor and is the first try of the metal from that chunk whose
  vein can start there. Every chunk around the anchor sees the same tries in the same order, so
  the vein comes out whole whatever order chunks generate in.
- When the anchor's column has generated (a postfix on `GenDeposits.GenChunkColumn`), it either
  holds the metal's ore (placed) or the spot failed and the next fallback whose column is not yet
  generated becomes active. With none left the cell has none. The state per (metal, cell) is
  `OreCellBook`, saved as `seraphhorizons:orecells`.

Engine facts: `GenDeposits` makes each source chunk's tries from the chunk's own seeded random, once
for every chunk within its reach being generated; only depth and host rock need terrain. IOG's
filter is static state reset every server start, which is why it had to go.

## Placer fields (#442)

- Cells of 1.5 km (`PlacerCellSizeMetres`), sixteen spots each (`PlacerCells`, same hash, its own
  salt; twice the ore cells' fallbacks because most columns lack water or a valley). The cell book
  is the ore cells' (`OreCellBook`, generalised to a spot count), plus where each field went
  (`PlacerBook`, `seraphhorizons:placercells`).
- Decided and placed when the active spot's column generates, at the TerrainFeatures pass (after
  terrain, soil layers and water; before plants), from that column alone. The field is kept inside
  the column (its centre at least its radius from the edges; at most 600 blocks one thick is a
  radius under 14), so no neighbour has to agree on anything.
- Suitability (`PlacerSite`): the dry part of the disc spans at most 8 blocks in height, at least
  two thirds of it is dry and 80% of that is soil, gravel or sand; and water stands within 8 blocks
  of the disc's edge, or the centre lies 2 or more blocks under the mean height of the column's
  edges (a valley floor). The nearest suiting centre to the spot is taken. Heights are the column's
  `WorldGenTerrainHeightMap` (the top solid block, which the game's surface deposits follow too);
  water is the fluid layer above it.
- The field: 300–600 blocks (seeded), a disc one or two thick, flush with the surface or one block
  under it, of `game:richgravel-{rock}` for the first block under the centre with a `rock` variant
  that has rich gravel. It replaces soil, gravel, sand or (the lower layer) stone, never ore.
- The scattered rich gravel deposit (Wilderlands Panning's `game:worldgen/deposits/rock/richgravel.json`,
  `disc-followsurface`, 75 tries per chunk, only in surface gravel) is cut to a quarter by a postfix
  on `GenDeposits.initAssets`.
- Pan tables: the pan takes the last key of `attributes.panningDrops` that matches the block
  (`BlockPan.CreateDrop`). Wilderlands Panning has a catch-all `@(richgravel)-.*` (native copper 3%)
  and fourteen per-rock tables; nine have no copper (conglomerate, limestone, peridotite, phyllite,
  slate, chalk, claystone, granite, shale), so each gets `nugget-nativecopper` at 1% on the server
  in AssetsLoaded (after the patch loader, as the pack's panning trim does).

## Hydrothermal districts (#441, #445)

Interesting Ore Gen's districts are the only source of gold and silver quartz and most chromite
and platinum. IOG 2.3.8 tiles the world in squares of the first config's
`minDistanceBetweenDistricts` and gives each tile a district with a hard-coded 40% chance, at a
point of the tile from the seed; a district is 1–3 km in radius, with major faults 2–4 km long.
`RarerDistricts` sets the tile to 7 km in every config (`patches/ore-rarerdistricts.json`): one
district per 122 km², against one per 40–90 km² in IOG's own 4–6 km tiles. Wave 1 had 10 km (one per
250 km²), sparser than the epic's one per 15 km tile; #445 settled on 7 km. The tiles are a pure
function of seed and tile, so `/sh ore districts` shows them before anything generates.

How district ore is made (IOG 2.3.8, `HydrothermalDistrict`, `HydrothermalDistrictSystem`):

- A district is built once per run, from the seed, when a chunk within a tile of it generates
  (`TrySpawnDistrictsNear`): its faults (5–10 major, 2–4 km; minor branches; 13–22 horsetail
  splays at each major tip), then its **ore zones** (`DetectOreZones`), 230–500 per district.
  Its ore never goes through the game's `GenDeposits` or IOG's `TiltedDiscDepositGenerator`, so
  neither the cell rule (`TryApproveOreSpawnSeed`) nor `DepositSizes` sees it.
  `HydrothermalDistrictSystem` places it itself in a `TerrainFeatures` pass: it lays the fault
  rock (the config's `hostMaterialCode`, replacing rock, gravel and sand), then turns zone blocks
  that are fault rock into ore.
- Zones: **ore shoots** at fault bends and widenings (an ellipse `oreShootRadius` ± variance,
  35 ± 10 blocks along the fault, half that across, density 1 − r², at least 10 segments of
  16 blocks apart on a fault), **ladder veins** (one-block slabs every 2–7 blocks across a
  widened 100–500 block stretch of fault) and **horsetail lenses** (1–4 ellipsoids of 4–24 blocks
  on a one-block fracture). Shoots and ladders reach from y 1 to the top of the world: every layer
  of rock under them is ore, which is why a vein held up to 13,000 ingots. Lenses hold a few
  ingots each.
- Each zone draws one ore from its type's pool by `weight`, and a second with even chance; a
  block becomes an ore with chance `geometric density × density`. The grade is the poorest nine
  times in ten except in a fault's 1–2 rich bands (`RichnessAt`). An ore whose
  `allowedVariantsByInBlock` has no entry for the district's rock places nothing (the geology
  add-on ores of the granite configs name basalt).
- The config sets fault and zone geometry per district type, not per ore, and has no field for a
  zone's height or for a number of zones per ore. Lowering an ore's `weight` hands its zones to
  the other ores (gold, gems), and lowering `density` only thins the ore into specks. So district
  veins are sized in code.

`SmallerDeposits` covers districts too (`Ore/Game/DistrictVeinSizes.cs`, rules in
`Ore/Core/DistrictVeins.cs`, data in `config/ore-districts.json`): a postfix on
`DetectOreZones`, before the zones are indexed, so each district is decided once from what IOG
builds from the seed, the same after every restart.

- Count: per district, at most 8 veins (shoots and ladders) hold each metal with a size target
  that is not one only districts provide; which ones is a stable hash of the district and the
  zone. The metal's ore in further veins and in every horsetail lens is swapped for a copy that
  places nothing (same weight, so the others' shares stay), and a zone left with nothing is
  dropped. Gold, silver, platinum and chromium keep all their zones.
- Size: each kept vein holding a sized metal is cut to a band of heights (its bounding box, which
  `GetOreBlocksInChunk` iterates). The ore a layer gives is estimated from the zone's geometry
  (a shoot: fault width × 4r/3; a ladder: stretch × widened width ÷ slab spacing), the ore's
  share and density, and the ingots of a block of its poorest grade (read from the block's drops)
  times 1.15; the band is the height that gives the size drawn for the zone (log-linear through
  the metal's small, typical and large at the 10th, 50th and 90th percentile), the least over its
  ores, placed by the hash between y 4 and sea level less 6, so it lies in rock. A vein whose
  whole column holds less keeps it.
- Unmanaged ores (gems, sulfur, halite, ...) keep their counts; in a vein that also holds a
  sized metal they share its band.

Before (the survey's seed 404 window, a granitic-deep district, radius 2.7 km): 35 bismuth, 41
tin, 12 lead and 9 zinc deposits of up to 13,000 ingots in 9.4 km², from 69–82 tin and bismuth
veins per granite district. After: see "Survey", "Districts after (#435)".

## Deposit registry (#443)

`OreSystem.Deposits` (`Ore/Game/DepositService.cs`), server side; null when neither ore cells nor
placer fields are bound in the world.

- **Candidates** from the seed and the cell books, no chunk generated: for each listed metal (managed
  by the cell rule and with a size range in `config/ore-sizes.json`, so not coal or the minerals)
  the cells a circle reaches, each cell's active spot (or, once measured, the ore's centre), and its
  record. A cell with no spot left has none. Gravel fields likewise, with the placed field's centre
  once generated.
- **Verify** generates and measures. An ore deposit whose anchor isn't generated yet is generated
  first (one column; the cell rule then places it or moves on, and the new active spot is followed,
  up to the spot count); then the anchor's column and its eight neighbours are loaded (generated if
  need be: `LoadChunkColumnPriority` per column, which calls back at once for a loaded column and
  otherwise on the main thread once it is fully generated), and the metal's ore blocks in them are
  counted. A block's worth is the sum over its drops of the average count times the dropped item's
  `metalUnits` (1.25 ore chunks plus 0.01 crystallised ore, as the survey reads them); 5 units make
  a nugget, 20 nuggets an ingot (`DepositSizing`). The tier is the bottom, middle or top third of the
  metal's `smallIngots`..`largeIngots`; below a tenth of `smallIngots` the deposit is worked out and
  marked sold out. The same count, per ore, grade and host rock (`OreTally`, `OreBlockKind` from the
  block's code `ore-{grade}-{ore}-{rock}`), is the deposit's **makeup** (#692, `DepositMakeup`):
  metal units per ore, blocks per grade and per rock, kept with the measurement. A gravel field's
  verify generates its column, which places it or not.
- **Checked** (`DepositCandidate.Surveyed`, #693): an ore deposit measured with its makeup (a
  measurement from before makeups were kept doesn't count), a gravel field placed. The seed gives a
  cell's spots, not which one takes the deposit or what ore it is (that follows the host rock, known
  once terrain generates), so traders offer maps only to checked deposits (`docs/trading.md`,
  "Deposit checks").
- **Registry** (`Ore/Core/DepositRegistry.cs`, JSON under `seraphhorizons:deposits`): per
  `DepositKey` (`copper:102,102`, `gravel:341,340`) the state `Unsold` → `Sold` (buyer's uid and
  name, game day) → `SoldOut`, and the last measurement (ingots, tier, centre, game day, makeup). A
  deposit is sold once; sold out is final but for an admin reset.

## Maps (#444)

Items `seraphhorizons:oremap` and `seraphhorizons:gravelmap`, class `ItemOreMap` (registered on both
sides by `OreMapsSystem`). Modelled on the game's `ItemLocatorMap`: everything is in the stack's
attributes (`depositId`, `metal`, `sizeTier`, `precision`, `ores`, `grades`, `rock`, `metals`, `x`,
`y`, `z`, the marker already offset), and right-click on the server adds a pinned waypoint for the
reader (icon `pick` or `rocks`; once per position) and keeps the item. Without the world map it says
the distance instead.

**What a map names (#692).** An ore map says the ore actually there, never the metal: its name,
waypoint and description come from the deposit's makeup. `ores`: the ores holding at least a tenth
of the deposit's metal (the richest always), richest first, at most three (`DepositMakeup.MainOres`;
argentiferous galena is named whenever a lead deposit holds any, taking the last place if need be,
since it is that deposit's silver, #690), named by `orename-{ore}` ("galena and cerussite"; `ItemOreMap.OreList`); `grades`: `only:poor` (90 %
of the graded blocks or more), `mostly:poor` (60 %), else `mixed:poor,medium`, the two commonest
(`GradeMix`, read "mostly poor", "poor and medium"); `rock`: the rock most of the ore sits in. The
size tier is the metal in the ground as measured above (each block worth its drops' `metalUnits`),
not what a given way of working the ore wins from it, and the description says so. So "Galena and
cerussite ore map", waypoint "Galena and cerussite deposit (medium)", and the lines "Ore: galena and
cerussite. Grade: mostly poor. Host rock: Limestone. Deposit size: medium, by the metal in the
ground". A gravel map names its field's rock and the metals the pan gives from that rock's rich
gravel (`metals`, `DepositService.PanMetals`: the pan's last `panningDrops` key that matches
`richgravel-{rock}`, each nugget's smelted metal, the likeliest first): "Gravel map (Granite)",
"The pan gives tin, silver, gold and copper here." A map made before keeps naming its metal.

Precision tiers (`MapPrecision`): 1 within 400 m, 2 within 150 m, 3 exact. The offset is a point of
the unit disc from the seed and the deposit key, scaled by the tier's reach, so every copy of a tier
agrees and a better tier lies between a worse one and the deposit. Gravel maps are always exact.

### For the traders (#455)

```csharp
var ore = sapi.ModLoader.GetModSystem<OreSystem>();
DepositService? deposits = ore.Deposits;   // null: no ore cells or placer fields in this world
MapIssuer? maps = ore.Maps;                // null with it

// Candidates near the trader, nearest first; Record.State tells sold / sold out.
List<DepositCandidate> copper = deposits.Candidates(x, z, radius: 6000, metal: "copper");
List<DepositCandidate> gravel = deposits.GravelFields(x, z, radius: 2000);
IReadOnlyList<string> metals = deposits.Metals;          // the metals that have deposits listed

// Before selling: generate and measure (async; the callback runs on the main thread, possibly at once).
deposits.Verify(candidate.Key, result =>
{
    // result.Status: Measured (Ingots, OreBlocks, Tier, WorkedOut), Field (gravel: Field), or None.
    if (result.Status == VerifyStatus.None || result.WorkedOut) return;  // sold out now: pick another
    ItemStack? map = maps.Issue(buyer, candidate.Key, MapPrecision.Fair); // marks it sold to buyer
    if (map == null) return;  // sold meanwhile, or no deposit
    // hand the stack over
});

// Without reserving (e.g. to show a price): maps.Build(key, precision) -> ItemStack?
// Registry by hand: deposits.Registry.Get(key) / MarkSold / MarkSoldOut / Reset.
```

- `Issue(IPlayer? player, DepositKey deposit, int precision)`: null and nothing changed if the
  deposit is sold or sold out, the cell has none, or the precision isn't 1–3. The size tier on the
  map is the last measurement, so verify first.
- A verify loads up to nine columns around the deposit (generating those that aren't): a few seconds
  each on a busy server. Don't verify every candidate: the traders verify only what a camp would
  offer, one check at a time as a player nears it (`Trading/Maps/Game/DepositSurveys.cs`, #693).
- Traders refuse maps offered for sale (the economy's `refused` prefixes); the items are ordinary
  otherwise.
- The traders' side is built (#455): `docs/trading.md` "Maps and leads". Prospectors offer one map
  per metal within 5 km, every trader a gravel map within 2 km, only to checked deposits (#693; the
  rest show "being surveyed" while a check runs, ahead of players nearing a camp); the sale
  reserves the deposit, verifies it again and issues the map, or refunds. `ItemOreMap` implements the game's
  `ITradeableCollectible` through `ItemOreMap.Hooks`, which the trading side sets.

## Argentiferous galena (#690)

Vanilla's silver galena (`galena_nativesilver`, "Native silver in galena") is renamed argentiferous
galena and is a lead ore: `OreMetals` lists it under lead, so lead deposits, their measurement and
their maps count it ("galena and argentiferous galena"), and silver deposits are silver quartz and
freibergite. With ore processing on, every form of it smelts to lead (its nugget is galena's,
`OreProducts.NuggetOf`), its loose ore drops a galena nugget, and its silver, 38 % of its lead
against plain galena's 3 % (`config/ore-processing.json`), is won only in the cupel (#722; the mod's
README, "Cupellation"). With the switch off the game hammers it into native silver as before.

**Worldgen is unchanged.** Interesting Ore Gen's galena subdeposit (sedimentary rock) and the
felsic districts' lenses (`config/hydrothermal/magmatic-felsic-shallow.json`) still place it. Two
places read an ore's metal for worldgen, and both keep it silver (`OreMetals.WorldgenMetalOf`): the
district rules, which keep silver's lenses and drop lead's (as lead, its lenses would vanish), and
an ore cell's anchor check. The deposit variants' codes (`galena`) never named it, so vein scaling
and the cell rule are as they were.

**The world's silver.** The 5-seed survey of #435 found silver galena 4.6 % of galena blocks, all
of it silver then. Galena now carries 3 % and argentiferous galena 38 %, so galena as a whole
carries 0.954 × 3 % + 0.046 × 38 % ≈ 4.6 % of its metal as silver: the same silver, now spread over
every lead deposit, more of it where the argentiferous ore is, and won at the cupel's 85 % by hand.
The survey has not been rerun for this (worldgen did not change, only how the ore is counted):
`config/ore-sizes.json`'s silver and lead medians were measured with it counted as silver
(`tools/ore-survey` counts it as lead from now on), and silver's 348-ingot median includes the
galena subdeposits; the factors stand, since the veins they scale are unchanged. A rerun would give
lead's and silver's medians as now counted.

## Survey (#445)

Measured with `tools/ore-survey` (its README has the method): the full pack at the pins of
2026-10-06 with the branch's `seraphhorizons`, seeds 101, 202 and 303 (then 404 and 505), each a
96 × 96 chunk square (3.07 km, 9.4 km²) at the map centre. *Before* is the epic's scan of the same
seeds at 160 × 160 chunks, without any of this (`build/orescan-pack-s*` in the main checkout).
*First* is the branch as wave 3 left it, *tuned* after #445's changes (below).

**What a window this size can show.** 96 chunks put the whole window inside one 5 km cell per
metal (cell 102, 102 for every seed), so each seed shows at most one anchored deposit per metal,
and ore densities per km² mean little: the cell's deposit is in the window or not. The deposits
below are matched to the server log's `Ore cells: ... deposit placed` lines by position, so a
managed vein is told apart from district ore and from the epic's merged deposits. Counts per
25 km² need windows of several cells: a 160 chunk square still holds one to four cells per metal
(about 35 minutes per seed); a 320 square, 4–9 cells, costs four times that.

### Anchored veins (ingots, the ore of the cell's one deposit)

| Metal (target) | Seed | First | Tuned |
|---|---|---|---|
| Copper (400) | 101 | 750 | 185 (deep, 64 under the surface) |
| | 202 | 925 | 445 |
| | 303 | 20 | under 20 |
| | 404 | – | 529 |
| Iron (400) | 303 | 2,590 | 1,011 |
| Bismuth (150) | 101 | 406 | 384 |
| Zinc (150) | 202 | 276 | 131 |
| Platinum (150) | 202 | 397 | 293 |
| Tin (150) | 303 | 43 | 43 (factor unchanged) |

First: median 2.1× the target over the eight veins (six of them 1.8–6.5×), so the five metals
measured above it got half their factor (`config/ore-sizes.json`, "factor"). Tuned: median 1.1×;
iron and bismuth still about 2.5× on one vein each. Bismuth barely moved: IOG's bismuthinite is a
chimney already cut to one tendril, so the factor goes on its length, and halving that changed
little of what the survey counted; worth a closer look. Tin's one vein is a deep seam at 0.3×. Lead, nickel, silver,
gold, titanium and chromium had no anchored vein in any window: their factors are unmeasured.

### Which spot the deposit took (tuned, five seeds)

The first spot held a vein for copper 5 of 5, bismuth 2/2, tin 1/1, iron 1/4, zinc 1/3, platinum
1/3, and for lead 0/2, coal 0/2, borax 0/4, rhodochrosite 0/2 (spot 1 placed it once). A failed
first spot means the metal's tries from that chunk found no rock to start in; the next spot is
usually outside a 3 km window, so how far down the eight spots a cell goes, and how many cells end
with none, needs a larger survey or `/sh ore cells` in a played world.

### Before and after, per km²

| | Before (3 seeds, 79 km²) | First (3 seeds, 28 km²) | Tuned (5 seeds, 47 km²) |
|---|---|---|---|
| Copper ore, ingots per km² | 11,758 | 60 | 208 (22 outside districts) |
| Iron ore, ingots per km² | 10,934 | 92 | 88 |
| Tin ore, ingots per km² | 2,433 | 2 | 891 (all but 43 ingots in districts) |
| Copper pockets per km² (share at the surface) | 73 (61%) | 0.04 | 0.08 (75%) |
| Rich gravel fields per km² (median blocks) | – | 0.04 (354) | 0.23 (370) |
| Scattered rich gravel, blocks per km² | 330–640 (epic) | 138 | 124 |
| Coal deposits per km² | 0.22 | 0 | 0 |

The before runs did not record rich gravel; the epic's scratch scan had 330–640 blocks per km².
Surface copper is gone (four pockets in 47 km²). Coal and the minerals show only what a cell's one
deposit puts in a window: none of the windows held a coal anchor.

### Placer fields

Per placer cell that the survey decided (its active spot's column generated): first, 1 field and
3 cells with none; tuned, 13 fields and 2 with none (87%), after 1–4 spots each, 244–586 blocks,
median 370, all at the surface. Other cells' next spot lay outside the window, which is why the
per-km² figure (one per 4.3 km²) understates them. The first run's spots failed on dry columns
whose lowest point lay only 2–4 blocks under their edges, or on discs spanning 6–8 blocks; the
tuned rule allows 8 blocks of relief and 2 blocks of low ground (`PlacerSite`). Most remaining
failures are slopes (a planar slope's middle is level with its edges' mean), steep ground and lakes.

### Districts seen

Districts IOG built near each window (within a tile of generated chunks): first, 3, 2 and 4 (10 km
tiles); tuned, 7, 7, 5, 6 and 4 (7 km tiles). District ore reached the window in seed 303 (first)
and in 404 and 505 (tuned).

### Tuning made (#445)

- `patches/ore-rarerdistricts.json`: 7 km tiles (was 10 km).
- `config/ore-sizes.json`: copper, iron, bismuth, zinc and platinum at half their factor.
- `Ore/Core/PlacerCells.cs`: `MaxRelief` 8 (was 5), `ValleyDepth` 2 (was 4).

### Districts after (#435)

The same windows with district veins sized and counted (`districts-s404`, `-s505`, `-s101`;
seed 101's window holds no district ore either way). Deposits in the two windows with district
ore (18.9 km²), ingots p10 / median / max; before is `tuned`:

| Metal (target) | Before: deposits | p10 / median / max | After: deposits | p10 / median / max |
|---|---|---|---|---|
| Bismuth (60 / 150 / 400) | 47 | 288 / 1,414 / 13,318 | 9 | 71 / 276 / 609 |
| Tin | 51 | 106 / 504 / 7,449 | 6 | 54 / 213 / 912 |
| Lead | 14 | 262 / 390 / 1,482 | 7 | 76 / 102 / 627 |
| Zinc | 12 | 140 / 478 / 7,009 | 8 | 77 / 186 / 949 |

Seed 404 alone (the window of the survey above): bismuth 35 → 8 deposits, tin 41 → 4, lead 12 →
5, zinc 9 → 6. Ore of the four per km², 404 and 505 together: 10,100 → 450 ingots. The server log
names each district built near the windows with its veins per metal before and after: granite
districts had 60–95 tin and bismuth veins each, mafic ones 60–150 copper and iron, felsic ones
35–60 lead; all are 8 now, and gold (5–9), silver (3–15), platinum (5–6) and chromium keep theirs.
Medians are 0.7–1.8× the typical size and the largest up to 2.4× the large one: a vein's ore per
layer is an estimate (the fault's width varies along it, and rich bands raise the grade), and
deposits within 150 m are counted as one. Few veins reach the surface now (12–50%), since a band
lies under sea level.

### Open

- District vein sizes rest on 30 deposits in two windows; a wider survey would show whether
  `gradeAllowance` (`config/ore-districts.json`) needs raising.
- Lead, nickel, silver, gold, titanium and chromium sizes, and counts per 25 km² for every metal,
  need a survey of several cells (160 × 160 chunks or more, five seeds).
- Not walked in game yet: finding a gravel field by map, panning, buying an ore map and reaching
  the deposit.

### Re-running it

```sh
VINTAGE_STORY=$HOME/Games/vintagestory dotnet build mods-src/seraphhorizons -c Release
VINTAGE_STORY=$HOME/Games/vintagestory dotnet build tools/ore-survey -c Release
VINTAGE_STORY=$HOME/Games/vintagestory tools/ore-survey/run.sh --name tuned --dump \
  --mod build/seraphhorizons_<version>.zip
VINTAGE_STORY=$HOME/Games/vintagestory tools/ore-survey/run.sh --name tuned --size 96 -j 5 \
  --port 42591 --mod build/seraphhorizons_<version>.zip 101 202 303 404 505
python3 tools/ore-survey/ore_survey.py summary build/ore-survey/tuned-s{101,202,303,404,505} \
  --json build/ore-survey/tuned.json
python3 tools/ore-survey/ore_survey.py summary build/orescan-pack-s{101,202,303} --json build/ore-survey/before.json
python3 tools/ore-survey/ore_survey.py compare build/ore-survey/before.json build/ore-survey/tuned.json
grep -h "Ore cells: .*: \|Placer fields: cell" build/ore-survey/tuned-s*/Logs/server-main.log
grep -h "\[HydrothermalDistrict\] '" build/ore-survey/tuned-s*/Logs/*.log | sort -u
```

A 96 square takes 12–16 minutes per seed with five at once. The server log lists every cell
decision (`Ore cells: <metal> cell X, Z spot N at x, z: deposit placed | no vein, spot M is next`)
and placer decision (`Placer fields: cell X, Z spot N at x, z: <n> blocks of ... | unsuitable
(...)`); `/sh ore cells` and `/sh ore districts` give the same in a running world.

## Engine facts

- Vanilla `GenDeposits` is deterministic per chunk (position, radius, thickness, grade); only the
  Y level and host rock need terrain. IOG's `TiltedAnywhereDiscGenerator` is likewise deterministic
  apart from its spacing filter, static state reset every server start (replaced by the cell rule).
- Hydrothermal districts (`HydrothermalDistrictSystem`) are a pure function of seed and tile;
  radius 1–3 km, major faults 2–4 km; IOG builds a district only when a chunk within a tile of it
  generates, and keeps it in memory.
- Vanilla treasure maps use `ItemLocatorMap` with `ModSystemStructureLocator.FindFreshStructureLocation`
  and a per-region consumed list: the pattern the ore maps follow.
- Walking is about 4m55 per km, sprinting 2m27; a tamed elk at full gait about 1m30 (unverified).
- BetterEr Prospecting reads actual ore blocks, so prospecting matches whatever worldgen produces.
- The pan takes the last `panningDrops` key that matches the block (`BlockPan.CreateDrop`); see
  "Placer fields".
