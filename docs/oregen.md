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
  marked sold out. A gravel field's verify generates its column, which places it or not.
- **Registry** (`Ore/Core/DepositRegistry.cs`, JSON under `seraphhorizons:deposits`): per
  `DepositKey` (`copper:102,102`, `gravel:341,340`) the state `Unsold` → `Sold` (buyer's uid and
  name, game day) → `SoldOut`, and the last measurement (ingots, tier, centre, game day). A deposit
  is sold once; sold out is final but for an admin reset.

## Maps (#444)

Items `seraphhorizons:oremap` and `seraphhorizons:gravelmap`, class `ItemOreMap` (registered on both
sides by `OreMapsSystem`). Modelled on the game's `ItemLocatorMap`: everything is in the stack's
attributes (`depositId`, `metal`, `sizeTier`, `precision`, `rock`, `x`, `y`, `z`, the marker already
offset), and right-click on the server adds a pinned waypoint for the reader (icon `pick` or
`rocks`; once per position) and keeps the item. Without the world map it says the distance instead.

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
  each on a busy server. Verify when the player asks for the map, not for every candidate.
- Traders refuse maps offered for sale (the economy's `refused` prefixes); the items are ordinary
  otherwise.
- The traders' side is built (#455): `docs/trading.md` "Maps and leads". Prospectors offer one map
  per metal within 5 km, every trader a gravel map within 2 km; the sale reserves the deposit,
  verifies it and issues the map, or refunds. `ItemOreMap` implements the game's
  `ITradeableCollectible` through `ItemOreMap.Hooks`, which the trading side sets.
