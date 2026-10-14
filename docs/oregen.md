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
copper and tin are gone; hydrothermal districts are rarer.

The targets are ingots, not blocks. Since #863, with ore processing on, an ore item holds about a
quarter of vanilla's metal units (3 / 5 / 7 / 10 by grade, from 15 / 20 / 25 / 35;
`OreProducts.RawUnits`), so a deposit of the same ingots is four times the blocks: every graded
ore's IOG vein is scaled by 4 in volume on top of its metal's factor (`OreSizeTable.VolumeFor`,
`OreProducts.DepositBlocks`). District veins, sized from the ore's own units, are widened four
times in ore per layer (`DistrictVeinSizes.Widen`: an ore shoot four times as long along its fault
and no wider, a ladder vein's slabs about four times closer, IOG's spacings of 2–10 divided by 4, at
least 1), so a vein holds its drawn ingots in about the height of rock it had. Without that it
needed four to five times the height, and most thin veins hit their whole column (10 of 15 tin and
bismuth veins in the granite district of `OreCellsScenarios`, at a quarter of their ingots);
widened, 3 of 15 did. Those 3 kept IOG's whole column (y 1 to the top of the world), widened, and
the survey of #943 found what that held: tin and zinc veins of 7,000–9,400 ingots. Since #943 a band
taller than the room under sea level is that room, so no vein keeps its column. A horsetail lens of
a sized metal (gold and silver, whose lenses are kept) has no band; it is grown to four times its
ore (`DistrictVeins.LensRadii`). In a true fault rock (travertine) its ore is only where a lens
crosses the splay's one-block fracture, a disc of the two radii, so both radii double (4–12 by 3–8
become 8–24 by 6–16); in a host that is also the country rock (granite, basalt) the whole spheroid
can be ore, so only the horizontal radius doubles. Either way a lens holds the ingots it did. The
medians and factors in `config/ore-sizes.json` stay as measured at vanilla's units; measurement, size tiers and the trader maps work in ingots from the live units,
so they need no change. Worlds already generated keep their ore as placed: a deposit from before
holds a quarter of the ingots it did. `tools/ore-survey/targets.json` holds the
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
  on a one-block fracture; gold's and silver's at twice the radii since #863). Shoots and ladders reach from y 1 to the top of the world: every layer
  of rock under them is ore, which is why a vein held up to 13,000 ingots. Lenses hold a few
  ingots each.
- Each zone draws one ore from its type's pool by `weight`, and a second with even chance; a
  block becomes an ore with chance `geometric density × density`. The grade is the poorest nine
  times in ten except in a fault's 1–2 rich bands (`RichnessAt`). An ore whose
  `allowedVariantsByInBlock` has no entry for the district's rock places nothing
  (`ResolveOreCode` returns null), yet IOG still draws it for its share of the zone's blocks: the
  geology add-on ores of the granite configs (chalcopyrite, pyrite, cerussite, wulfenite, the
  beryls and topazes, `REQUIRES: geoaddons`) list only `rock-basalt`, copied from the mafic config,
  so a felsic or granitic-deep district places no copper or iron and none of that lead (#946; main
  is the same, it is IOG's config: the census of main's mod found 0 ingots in all 48 felsic copper
  and 54 iron veins).
- The config sets fault and zone geometry per district type, not per ore, and has no field for a
  zone's height or for a number of zones per ore. Lowering an ore's `weight` hands its zones to
  the other ores (gold, gems), and lowering `density` only thins the ore into specks. So district
  veins are sized in code.

`SmallerDeposits` covers districts too (`Ore/Game/DistrictVeinSizes.cs`, rules in
`Ore/Core/DistrictVeins.cs`, data in `config/ore-districts.json`): a postfix on
`DetectOreZones`, before the zones are indexed, so each district is decided once from what IOG
builds from the seed, the same after every restart.

- Ores that place nothing (#946): an ore whose code names no block in the host rock is taken out
  of its zone (and the zone's cached `TotalOreWeight` redone), so the ores that place share its
  blocks, and the count below never spends a vein on it. A zone left with nothing that places is
  dropped. The census table counts such veins no more: felsic districts hold no copper or iron.
- Count: per district, at most 8 veins (shoots and ladders) hold each metal with a size target
  that is not one only districts provide; which ones is a stable hash of the district and the
  zone. The metal's ore in further veins and in every horsetail lens is swapped for a copy that
  places nothing (same weight, so the others' shares stay), and a zone left with nothing is
  dropped. Gold, silver, platinum and chromium keep all their zones.
- Size: each kept vein holding a sized metal is cut to a band of heights (its bounding box, which
  `GetOreBlocksInChunk` iterates). The ore a layer gives is estimated from the zone's host rock
  per layer (a shoot: the fault rock IOG lays through it, counted from the fault's spline, and
  the share of the rest of it whose country rock is the host rock too, from the strata predicted
  there, below, #946; a ladder: stretch × widened width ÷ slab spacing, all fault rock), the
  ore's share of the ores that place and its density, and the ingots a block gives at that
  height: the mean of the ore's grades
  (each read from its block's drops) as IOG draws them at the fault's richness there
  (`DistrictVeins.ExpectedIngots`, #943). Before #943 it was the poorest grade times 1.25 (1.15
  before #863 widened the grades), which still stands in where the grades or the fault can't be
  read. The band starts at a height from the hash between y 4 and sea level less 6, so it lies in
  rock, leaving room for the most height it can need (all of it away from rich bands), and grows a
  layer at a time until each of its metals (all its ores of that metal together) holds the size
  drawn for the zone, the richer of two then thinned to its size by cutting its ores' density in
  that zone (#946: the band used to stop at the first, so the other, tin beside bismuth, always
  fell short; stopping where they average out pushed zinc and lead 30–40 % over) (log-linear through the
  metal's small, typical and large at the 10th, 50th and 90th percentile). A band taller than that
  room is the room (#943): the vein holds less than drawn. Before #943 such a vein kept IOG's whole
  column, still widened.
- Rich bands (#943): each fault has 1–2 bands of height, 15–30 blocks each way, where IOG draws
  most blocks at the richest grades (`RichnessAt`). Cassiterite's grades are 1 / 3 / 4 / 6 units,
  so a vein inside a rich band holds about three times the ore of one outside: seed 202's largest
  tin vein after the host fix, 1,148 ingots against a drawn ~490, was one shoot with y 24–47 in
  its fault's rich band (2.9 units a block on average against the 1.25 allowed). Reckoning each
  layer's grades makes such a band thinner, not the vein richer.
- Host rock (#943, #946): IOG turns a zone block into ore only where it is the district's host
  rock: the fault rock it lays along its faults (rock, gravel or sand replaced by
  `hostMaterialCode`), and, where the host is also one of the world's rocks (granite in the
  granitic and felsic districts, basalt in the mafic), the country rock that happens to be it.
  #943 counted a shoot whole wherever the host was a stratum; the census of #946 showed that wrong
  both ways: mafic veins held a tenth of their drawn size (copper median 10 ingots), since basalt is
  1–5 % of their bands. Now each part is measured:
  - **The shoot's fault rock** (`Ore/Core/ShootShape.cs`): IOG's shoot (`OreShootSampler`) is an
    ellipse in a sheared frame, `s` along the bend segment's own tangent and `t` along the fault's
    global across direction (perpendicular to its overall heading, the direction it dips in), at
    density 1 − (s/a)² − (t/b)², cut to its box (centre ± a + 2). A shoot sits at a bend, so its
    tangent turns 10–30° from the heading and the fault, which curves on, leaves the ellipse
    within a few dozen blocks of the centre: along the tangent the ellipse reaches only
    1 / √(1/a² + c²/b²), c = tangent · across. The fault rock is counted from the fault's spline
    (`HydrothermalFaultSpline.PlaceInChunk`: at every block of its length, a ray down from each block
    across within its half width there, tapered over its ends and widened along a ladder stretch;
    each ray steps `dip` across a layer, and where it moves into a new column it lays the column it
    leaves too, at the same height, which fills the gaps of a fault at a slant to the block grid:
    leaving that out undercounted the fault rock by about a sixth), at eight depths in the band's
    range, averaged, each block's centre moved back up the dip, where the shoot's density is read
    (the shoot and its fault dip together along the global across). #943's "fault width × 4r/3" assumed the fault ran along the strike: for a shoot
    widened four times along its strike (#863) that was up to six times its fault rock.
  - **The country rock** (`Ore/Game/RockStrataPrediction.cs`): the game's strata are a function of
    the seed, so they are replayed for a column before it generates: `GenRockStrataNew.genBlockColumn`
    (game 1.22.7, Terrain pass), step for step, with the live generator's strata
    (`worldgen/rockstrata.json` as GeologyAdditions and MaterialNeedsGeology patch it: 40 strata),
    its per-stratum noise layers and its distortion noise, read by reflection, the region's strata
    map cells (`MapLayerCustomPerlin`, 16 blocks a cell, lerped as `GetIntLerpedCorrectly`) and the
    geologic province map (`GenMaps`' province layer, smoothed as the strata do), rebuilt with its
    position seed kept local, since the game's own noise object keeps state between calls. Strata
    are laid in config order, bottom-up ones (kimberlite, phyllite, andesite, peridotite, igneous
    basalt, granite, gabbro, the metamorphics, monzonite, syenite, pyroxenite, ...) from y 1 up and
    top-down ones (basalt as a volcanic, the sedimentaries, bauxite, slate, diorite, ...) from the
    surface down, each as thick as its noise allows within its rock group's thickness for the
    province; what neither reaches stays the default rock, granite. Against generated columns
    (Atlas, seeds 1101, 1103, 1105, 48 chunk columns, 3,072 block columns) the replay matches
    99.6–99.9 % of strata blocks, the rest deposits and structures. The one input not known before a
    column generates is its surface height (`WorldGenTerrainHeightMap`, from the landforms): the
    top-down strata hang from it, and so does how far down they reach. So each column is predicted
    at five heights, the 10th to 90th percentile of the pack's terrain (`surfaceAboveSeaLevel` in
    `config/ore-districts.json`: sea level −34, +13, +25, +31, +50), and the shares averaged; that
    is the approximation. Over the band (y 4 to sea level − 6) it gives granite 12.8 / 30.3 /
    49.3 % against 13.9 / 33.1 / 50.5 % in those columns, an 8-layer slab off by 1–5 points on
    average. Basalt is 0.0–0.3 % of the band (it is a top-down volcanic stratum, near the surface,
    or an igneous one below granite in the stack), which is why mafic shoots are their fault rock.
    A shoot's share is sampled at about 24 points of its plan, density-weighted, a column every
    8 layers shifted by its dip, only for the kept shoots of sized metals (lazily, when the band is
    grown); the province maps and strata cells are cached. A district is sized in a few hundred
    milliseconds (the log line's `sized in`).
  - **Wider shapes**: with the fault rock counted honestly, a thin shoot (a fault-rock host, or
    a sheared one) often needs more height than the room under sea level holds: a third of the
    sized veins in the Atlas districts. Its band would then be the room and hold less than drawn.
    So a widened shoot whose band is the room and still short takes the first of four wider shapes
    (two, four, eight and sixteen times its own widened area: 8 to 64 times IOG's) whose band holds
    its size, or the widest; each splits the area between strike and across (strike at most
    sixteen times IOG's, across eight) the way that keeps the most fault rock in it, its box what the sheared ellipse
    needs. Where it is all fault rock this is a wider vein along the fault, never a blob in
    country rock that places nothing.
  - **Horsetail lenses**: a lens whose centre column's predicted strata are mostly host rock over
    its height is grown across only (the whole spheroid is ore, #943); every other lens both ways
    (#863: its ore is the disc on its fracture).
  - What it misses: the surface height (the mixture above), deposits and structures that replace
    strata later, other faults' fault rock crossing a shoot, zones overlapping (IOG gives a block to
    the first zone that draws it, even an ore that places nothing), and fault rock above the surface
    or in caves (IOG only replaces rock, gravel and sand).
  A ladder needs no change: IOG lays fault rock across its whole widened stretch, the region its
  slabs fill. Moving the granite districts' host to a fault rock instead was ruled out: no rock in
  the pack has ore blocks for every ore they place (granite is the only one with cassiterite,
  bismuthinite, gold and silver quartz, sphalerite, both galenas, cinnabar, sulfur, alum and
  quartz; quartzite has only the two quartz ores and quartz, monzonite lacks cassiterite, cinnabar
  and alum), and IOG names the rock in each ore's code.
- Unmanaged ores (gems, sulfur, halite, ...) keep their counts; in a vein that also holds a
  sized metal they share its band.

Before (the survey's seed 404 window, a granitic-deep district, radius 2.7 km): 35 bismuth, 41
tin, 12 lead and 9 zinc deposits of up to 13,000 ingots in 9.4 km², from 69–82 tin and bismuth
veins per granite district. After: see "Survey", "Districts after (#435)".

## Grade within veins (#944)

**How IOG grades a vein.** `TiltedDiscDepositGenerator.GenDeposit` draws one grade index per vein,
`floor(r² · MaxGrade)` from the try's `DepositRand` (`MaxGrade` the longest per-rock grade list of the
variant): poor 50 %, medium 20.7 %, rich 15.9 %, bountiful 13.4 % for a four-grade ore (57.7 / 23.9 /
18.4 with three, 70.7 / 29.3 with two). Every vein shape hands that index to `TryPlaceMainOre(accessor,
pos, index)` per block, which takes `Blocks[min(len - 1, index)]` from the grades the host rock allows
(`placeBlockByInBlockId`, from `allowedVariantsByInBlock`). IOG declares `GradeDistribution` and never
reads it. The only other grades in a vein are a chimney's core (rich, 90 % of its inner half radius)
and the prospect veins (`TryPlaceTracerOre`, always the poorest). The cell rule approves one vein per
metal per cell, so before this every cell deposit was at least 80 % one grade (8 seeds, 26 deposits).

**Pockets** (`GradePockets`, new worlds only, recorded as `gradePockets` in
`seraphhorizons:oreworld`): a Harmony prefix on `TryPlaceMainOre` (`Ore/Game/GradePocketPlacement.cs`)
rewrites the index per block from the drawn one and the block's position (`Ore/Core/GradePockets.cs`).
Space is cut into pockets about `GradePocketSizeBlocks` (8) across, the cells of a jittered lattice
of points (a Worley pattern), each with a value in [0, 1) from the world seed; a block in a pocket
whose value is below the poorer share goes one grade poorer, above one minus the richer share one
richer, else it keeps the drawn grade. A pocket's value is uniform whatever its size, so on average
those shares of a vein's blocks move. It draws nothing from `DepositRand` (the vein's shape and
everything after must replay alike in every chunk pass) and depends on the seed and the position
alone, so a vein crossing a chunk border keeps its pockets whole. Sub-deposits, prospect veins and
district veins (which draw grade per block already) are left alone.

The shares keep the **vein's mean metal units** at its drawn grade's. With `GradePocketSpread`
(0.35) the share off the drawn grade where it can go both ways, units `u` by grade, and the drawn
grade `g`: richer `s · (u[g] − u[g−1]) / (u[g+1] − u[g−1])`, poorer the rest of `s`
(`GradePocketWeighting` `units`; `index` splits it evenly). The prefix reads the units from the
host rock's blocks (`ore-{grade}-{ore}-{rock}`, `OreProducts.RawUnits`) and the range from the same
entry IOG uses, so a vein capped by its rock is graded against the cap. At 3 / 5 / 7 / 10:

| Drawn (in its rock) | Poorer | Same | Richer | Mean units | Drift |
|---|---|---|---|---|---|
| poor (any rock with medium) | – | 70 % | 30 % | 3.6 | +20 % |
| medium, rock has rich | 17.5 % | 65 % | 17.5 % | 5 | 0 |
| rich, rock has bountiful | 21 % | 65 % | 14 % | 7 | 0 |
| bountiful | 30 % | 70 % | – | 9.1 | −9 % |
| rich, the rock's cap | 30 % | 70 % | – | 6.4 | −8.6 % |
| medium, the rock's cap | 30 % | 70 % | – | 4.4 | −12 % |
| any, a one-grade rock | – | 100 % | – | as drawn | 0 |

A vein at either end of its rock's grades can only go one way, which moves its mean: it gets
`GradePocketEndSpread`, 0.3 (the owner's ruling, 2026-10-13: variation in every deposit is worth
the drift; #945's iron and copper retune absorbs it; 0 keeps such veins at one grade). Poor veins
are about half of all four-grade veins, so a typical metal's ore in the ground rises by about a
tenth. Hematite (4 / 6 / 8 / 11: poor +15 %, bountiful −5.5 %), cassiterite and gold quartz
(1 / 3 / 4 / 6: poor +60 %, bountiful −10 %) and silver quartz (2 / 4 / 6 / 7: poor +30 %, bountiful
−8.6 %) use their own units. With ore processing off the same weights are used: vanilla's units are
four times these but for rounding.

**What reads it.** Deposit measurement, the makeup's `GradeMix` (#692) and item values read each
block's own grade, so they follow. A deposit no longer reads `only:` unless its rock has one grade
or it is a pocket or two in size: a medium or rich one reads `mostly:` (65 % at the drawn grade, less
its prospect veins) or `mixed:`, a poor one `mostly:poor` (70 %, plus its poor prospect veins), where
it read `only:poor`. `GradePocketsScenarios` checks it on real deposits: every block of a deposit
matches one drawn grade at its position, in every chunk column (each its own chunk pass), and at
most a quarter of the deposits of 5,000 blocks or more are 80 % one grade (a vein crosses a few dozen
pockets at most, so one can by chance: seed 944944's poor bismuth deposit came out 85 % poor). A
deposit no pocketed column fits is district ore, skipped.

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

**Under water.** A measurement also records whether the deposit is under water
(`DepositRecord.Underwater`, `DepositWater`): more than half of the columns its ore is in have more
than 2 blocks of water or ice (any `*water` liquid, lake or glacier ice, in either layer) standing
over their worldgen terrain height. Its map and offer are named so ("Galena ore map (underwater)",
waypoint "Galena deposit (medium, underwater)", the trade window's row "Galena (underwater)"), never
with a depth, and sell at `underwater` (0.1) times the price (`map-prices.json`). A deposit measured
before this has no value and counts as dry until it is measured again (a sale re-measures it).

Precision tiers (`MapPrecision`), named to players: 1 rough, within 150 m; 2 fair, within 50 m; 3 fine, within 15 m. The offset is a point of
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

### Districts after #943

Seeds 202 and 505 at 160 × 160 chunks (52.4 km²), with every pinned mod, against #863's runs of the
same seeds (`build/ore-survey-863/pack-s*` in the main checkout). The deposits that are not a
cell's anchor (matched to the log's `Ore cells: ... deposit placed` lines), all within 3.1 km of a
`magmatic-granitic-deep` district, ingots:

| Metal (60 / 150 / 400) | #863 | Host fix | With grades by layer |
|---|---|---|---|
| Tin | 9,375, 7,296, 993, 915, 166, 124, 77 | 1,148, 192, 88, 81 | 537, 192, 88, 81 |
| Zinc | 7,017, 5,475, 3,650, 2,052, 635, 186, 94, 81, 74 | 292, 248, 77, 51 | 354, 182, 78, 62 |
| Bismuth | 3,957, 3,622, 1,899, 1,636, 1,317, 271, 245 | 273, 123, 105, 58 | 232, 138, 123, 58 |
| Lead | 2,072, 668, 473 | 92 | 110 |
| Gold (30 / 80 / 200) | 455, 363, 190, 88 | 233, 198, 111 | 233, 198, 111 |

Median 954 → 105 → 123 for the four base metals together; 17 of 26 were over the large size,
then 1 of 13, now 1 of 13 at 537, inside the largest a vein draws (about 510, the 100th
percentile on the same log-linear slope; the survey also counts the 1% crystallised ore). The
host fix alone left that tin vein at 1,148: one shoot (rebuilt in an Atlas scratch run: no other
tin shoot within 360 m) whose band crossed its fault's rich band, 2.9 units a block against the
1.25 allowed; reckoning each layer's grades cut its band from 37 layers to 13. Veins away from
rich bands come out a little larger than with the flat allowance (the poorest grade's mean at
richness 0 is under 1.25 times it for most ores). Smaller veins dropped under the survey's
50-ingot floor, so fewer are counted. Neither window held district copper or iron (their mafic
districts lie outside), so mafic veins were not measured here.

### Districts after #946

A census (`tools/ore-survey/run.sh --census --fast --kinds districts`) of seeds 1101–1110, 640 ×
640 chunks each, every district vein of a sized metal centred in the area (horsetail lenses left
out), with every pinned mod: before is #943 (the ten-seed census that found the mafic, felsic and
granitic-deep veins wrong), after is #946. Ingots per vein, every vein (small ones too):

| District, metal (small / typical / large) | Veins before → after | Before p10 / median / p90 | After p10 / median / p90 | After median ÷ typical |
|---|---|---|---|---|
| cold copper (150 / 400 / 1000) | 58 → 56 | 46 / 199 / 680 | 127 / 479 / 2,656 | 1.20 |
| felsic bismuth (60 / 150 / 400) | 90 → 87 | 0 / 22 / 157 | 39 / 152 / 561 | 1.01 |
| felsic copper (150 / 400 / 1000) | 61 → 0 | 0 / 0 / 0 | – | – |
| felsic gold (30 / 80 / 200) | 101 → 94 | 0 / 11 / 109 | 26 / 83 / 211 | 1.03 |
| felsic iron (150 / 400 / 1000) | 79 → 0 | 0 / 0 / 0 | – | – |
| felsic lead (60 / 150 / 400) | 212 → 93 | 0 / 0 / 264 | 37 / 166 / 990 | 1.11 |
| felsic silver (30 / 80 / 200) | 105 → 103 | 0 / 14 / 152 | 24 / 104 / 380 | 1.29 |
| felsic zinc (60 / 150 / 400) | 98 → 97 | 0 / 29 / 316 | 52 / 208 / 524 | 1.39 |
| granitic-deep bismuth (60 / 150 / 400) | 50 → 50 | 0 / 20 / 261 | 60 / 132 / 447 | 0.88 |
| granitic-deep copper (150 / 400 / 1000) | 8 → 0 | 0 / 0 / 0 | – | – |
| granitic-deep iron (150 / 400 / 1000) | 20 → 0 | 0 / 0 / 0 | – | – |
| granitic-deep lead (60 / 150 / 400) | 68 → 50 | 0 / 19 / 246 | 51 / 137 / 422 | 0.91 |
| granitic-deep tin (60 / 150 / 400) | 53 → 50 | 0 / 18 / 341 | 60 / 124 / 915 | 0.82 |
| granitic-deep zinc (60 / 150 / 400) | 49 → 49 | 0 / 74 / 447 | 70 / 207 / 472 | 1.38 |
| mafic chromium (60 / 150 / 400) | 2 → 2 | 121 / 202 / 202 | 121 / 202 / 202 | 1.35 |
| mafic copper (150 / 400 / 1000) | 32 → 31 | 0 / 12 / 92 | 110 / 313 / 1,197 | 0.78 |
| mafic iron (150 / 400 / 1000) | 36 → 35 | 0 / 32 / 228 | 117 / 482 / 1,362 | 1.21 |
| mafic lead (60 / 150 / 400) | 35 → 31 | 0 / 8 / 50 | 28 / 237 / 1,424 | 1.58 |
| mafic nickel (60 / 150 / 400) | 35 → 34 | 0 / 13 / 96 | 60 / 162 / 602 | 1.08 |
| mafic platinum (60 / 150 / 400) | 23 → 22 | 0 / 10 / 38 | 38 / 112 / 256 | 0.75 |

- Felsic and granitic-deep copper and iron are gone: their ores never placed (IOG's config, above),
  and the zones' blocks go to the ores that do. Felsic lead went from 212 veins to 93 for the same
  reason (cerussite and wulfenite name basalt; only galena places).
- The estimate is unbiased: matching each vein's estimate (the debug log's `the band holds N by
  the estimate`) to its census, every district type and metal holds its draw (1.00 at the median)
  and measures 1.01–1.16 times its estimate (about 900 veins); 1.15–1.30 before the fault rock was
  counted at depth (IOG's rays fill a slanted fault's gaps), and ore shoots counted whole or as
  their fault's width × 4r/3 before that.
- Two sized metals in one vein (tin and bismuth, gold and lead, copper and iron): the band grows
  until the poorer holds its size and the richer one's ore is thinned to its own. Stopping at the
  first left the other short (granitic-deep tin 0.65 of typical); stopping where they average out
  pushed zinc and lead 30–40 % over.
- Four groups land over typical, and are accepted as they are (owner's ruling): felsic silver
  (1.29) and zinc (1.39), granitic-deep zinc (1.38) and mafic lead (1.58). Each of their veins holds
  its draw (held ÷ drawn 1.00, measured 1.02–1.09 times its estimate); the census counts deposits,
  not veins: `ore_survey.py` joins fragments of one metal whose centres are closer than 150 m
  (`--link`), and a district's 8 kept veins of a metal often sit that close along one fault, so two
  or three count as one larger deposit. That is how a player finds them too: one long run of ore
  along the fault. The sizes drawn also scatter: about 50 veins per district type and metal draw
  their median within ±16 % of typical two times in three.
- Mafic platinum stays at 0.75 of typical, accepted (owner's ruling): nativeplatinum and sperrylite
  weigh 0.0325 in their zones against about 1.0 for copper and iron, so a vein's platinum is a
  sliver of its blocks, and 16 of its 24 veins are cut to the room under sea level even at the widest
  shape (64 times IOG's area), holding 0.78 of their draw at the median.
- Sizing a district takes 31–877 ms, median 193, over the census's 139 districts (the log's
  `sized in`), once per district per run.

### Open

- The surface heights the strata are predicted at (`surfaceAboveSeaLevel`) come from three seeds'
  terrain near the map centre; a world type or landform mod that moves the terrain would want them
  measured again (the Atlas scenario `District_country_rock_is_predicted_from_the_strata` checks the
  replay itself).
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
# District veins only (#946): a census, and each vein's estimate in the debug log
VINTAGE_STORY=$HOME/Games/vintagestory tools/ore-survey/run.sh --census --fast --kinds districts \
  --name d --size 640 -j 3 --port 42650 --mod build/seraphhorizons_<version>.zip 1101 1102 1103 1104 1105 1106
python3 tools/ore-survey/ore_survey.py census build/ore-survey/d-s110{1,2,3,4,5,6} \
  --dump build/ore-survey/<name>-dump/orescan.json --json build/ore-survey/d-census.json
grep -h "the band holds" build/ore-survey/d-s*/Logs/server-debug.log
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
