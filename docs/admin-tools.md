# Admin tools

Debugging tools for server admins (#458 ore, #459 traders), in the pack's own mod: `Admin/` (what
both trees share), `Ore/Game/Commands/OreAdminCommands.cs` and `Trading/Admin/`. The command table
is in the mod's README ("Admin tools"); this note covers the conventions, the JSON shapes, the state
export, the logs, the map layer and the hooks later waves use. Switch `AdminTools` (default on).

## Conventions (`Admin/AdminCommands.cs`)

`AdminSystem` (ExecuteOrder 1, after every feature has registered its commands) installs one
precondition on the `/sh ore` and `/sh trade` nodes. The game runs a node's preconditions when a call
reaches it, before its subcommands are looked up and before any parser runs, so the precondition can:

- take every `--json` out of the raw arguments and remember it for the call (a `ConditionalWeakTable`
  keyed by the call's `TextCommandCallingArgs`, which the game passes down the whole tree);
- walk the node's subtree, the first time a call reaches each command, setting privilege
  `controlserver` where a command has another or none (the game checks only the privilege of the
  command whose handler runs, or its nearest parent's, so a subcommand that set none would take what
  the shared `/sh` root got from whichever feature registered first), and wrapping its handler.

The wrapper runs the handler and, with `--json`, replaces the answer's text with one JSON object.
Handlers that know their data call `AdminCommands.Attach(args, JsonObject)` (or build an
`AdminOutput` and return `AdminCommands.Answer(args, output)`); the rest still answer in JSON, with
the generic fields only. Commands registered later (orders, deliveries, the map shop, visitors) get
all of it without doing anything. The handler is swapped through the game's private
`ChatCommandImpl.handler` (1.22.7); if that field goes, `--json` is still stripped and commands answer
in text, with a warning at start.

Every answer under both nodes, text or JSON, goes through `AdminOutput.ChatSafe`: the game
`string.Format`s a player's single-line answer through `Lang.GetL` (an answer with a newline is sent
as is), so a brace, which every JSON answer has, logged `Input string was not in a correct format`.
Such an answer gets a trailing newline; escaping the braces would double them for the console. Atlas
formats every answer through `Lang.Get` regardless, so the admin scenarios check the raw answer
against the game's rule and ignore the harness's own log line (`tests/PackTests/FormatErrorWatch.cs`).

Tab completion: the 1.22 client completes no server command arguments. Fixed words use the game's
`WordRange` / `OptionalWordRange` parsers, which validate them and list them in `/help`; metals use
`Word(name, suggestions)`.

## JSON answers (`Core/AdminOutput.cs`)

Every answer:

```json
{ "command": "ore list", "ok": true, "summary": "3 deposits within 6000 blocks of 0, 0:",
  "lines": ["copper:1,2 spot 0 at ...", "..."], ...command fields }
```

`command` is the path under `/sh`; `ok` is false for an error (the game's status stays Error too);
`summary` and `lines` are the text answer split at its first line. A command field never replaces
`command` or `ok`. Command fields (absent ones are the generic shape only):

| Command | Fields |
|---|---|
| `ore cells` | `radius`, `origin {x, z}`, `cells[] {kind, cell {x, z}, active, placed, none, spots[] {index, x, z, status}, field?}`; `status` is `waiting`, `placed`, `failed`, `consumed`, `pending` |
| `ore cell` | `metal`, `cell`, `cellSize`, `managed`, `active`, `placed`, `spots[]` |
| `ore here` | `x`, `z`, `deposits[] {metal, cell, spot, x, z, distance, bearing, status}` |
| `ore list` | `radius`, `metal`, `filter`, `deposits[]` (below) |
| `ore gravel` | `radius`, `fields[]` (deposit rows plus `rock`, `blocks`) |
| `ore verify` | `verify {status, id, oreBlocks, ingots, tier, workedOut, ores, grades, rock, x, y, z, seconds}` when answered at once (`ores`, `grades`, `rock`: what the ore is, #692) |
| `ore count` | `radius`, `columns`, `unloadedColumns`, `blocks`, `rows[] {metal, grade, blocks, units, ingots}` (`grade` `-` for ungraded ores) |
| `ore districts` | `tileSize`, `tiles[] {tile {x, z}, district, centre, built, config, radius, majorFaults, minorFaults, horsetails, oreZones}` |
| `ore markers` | `added`, `deposits[] {id, x, z, state}`; with `clear`: `removed` |
| `ore survey` | `file`, `cellsFile`, `origin {chunkX, chunkZ}`, `chunks` |
| `ore registry export\|import` | `file`, `records`; `clear`: `removed` |
| `ore log`, `trade log` | `file`, `channels[]` (those on) |
| `ore map`, `trade map` | `on`, `drawn` (things drawn, -1 if refused); trade: `item` |
| `trade camps` | `radius`, `camps[] {id, type, status, x, y, z, distance, region, settlementReserve}` |
| `trade inspect` | `trader {id, entityId, type, climate, rock, x, y, z, wallet, sideBudget, nextRestockDays, supplyRegion}`, `selling[]`/`buying[] {slot, code, key, core, stackSize, stock, price}`, `standing[] {player, uid, tier, effective, personal, company, spill}`, plus contributors' fields |
| `trade restock` | `full`, `wallet`, `sideBudget`; `reroll`: `selling[]` codes; `wallet`/`budget`: `wallet`, `sideBudget` |
| `trade value` | `code`, `value`, `effective`, `floorZero`, `source` (`direct`, `family`, `missing`), `family`, `members` |
| `trade values missing` | `listed[]`, `creative[]` (codes) |
| `trade values suspicious` | `belowIngredients[] {code, value, ingredients, recipe}` |
| `trade supply [item\|all]` | `region`, `day`, `items[] {item, level, factor, decayPerDay, last {day, kind, delta}}` |
| `trade maps` | `accepted`, `candidates[] {id, distance, state, tier, generated, accepted, reason}` |
| `trade export` | `file`, `sections[]`; `import`: `file`, `sections {name: outcome}` |

A deposit row: `{id, kind, spot, x, y, z, distance, generated, state, soldTo, soldAtDays, ingots, tier}`.

## State export (`Core/AdminState.cs`)

```json
{ "format": "seraphhorizons-admin-state", "version": 1, "exported": "2026-10-06T12:00:00Z",
  "world": "<savegame id>", "seed": 123,
  "sections": { "supply": {...}, "standing": {...}, "deposits": [...] } }
```

Each section is its system's own save format: `supply` is `SupplyBook.ToJson` (`day`, `regions`),
`standing` is the `StandingState` the savegame holds (`Players`, `Companies`, `Designated`), `deposits`
is `DepositRegistry.Serialize` (rows `{id, record}`). Import replaces the sections the file has, one by
one, and leaves the others alone; a section that fails to read is reported and changes nothing. A
section of a system that isn't on in this world is "unknown" and skipped.

A system adds its section on `TradingSystem.AdminState`:

```csharp
TradingSystem.Of(api)!.AdminState.Register(new DelegateAdminState("orders",
    () => JsonNode.Parse(book.ToJson())!,
    data => book.ReplaceWith(OrderBook.FromJson(data.ToJsonString()))));
```

`IAdminState { string Section; JsonNode Export(); void Import(JsonNode) }` uses System.Text.Json's
`JsonNode`, not Newtonsoft's `JToken`: the interface lives in `Core/`, which is BCL only and unit
tested without the game.

## Logs (`Admin/AdminLogs.cs`, `Core/AdminLog.cs`)

`Logs/seraphhorizons-ore.log` (channels `placement`, `verify`) and `Logs/seraphhorizons-trade.log`
(`supply`, `standing`, `orders`, `deliveries`, `maps`, `visitors`), every channel off at start. The
game's loggers write a fixed file per log type and take no new ones (`ServerLogger.getLogFile`), so
these are plain appending files in `GamePaths.Logs`, opened on the first line written. Lines:
`2026-10-06 14:03:11 [supply] 2,0 game:ingot-iron sold to smith x16: level 6.4`.

Written today: ore cell and placer field decisions (`OreCellPlacement`, `PlacerFields`, worldgen
threads: the log locks), verifications (`DepositService.Measure`), supply changes from deals
(`EconomyPatches`), standing from deals (`StandingSystem.OnDeal`). Later waves write their channel
with one line: `SeraphHorizons.Mod.Admin.AdminLogs.Trade?.Write("orders", "...")` (null with the
admin tools off; a channel that is off costs a lock and a set lookup).

## Map layer (`Admin/AdminMap.cs`)

`AdminMapLayer` is registered with the game's `WorldMapManager` on both sides (`RegisterMapLayer`, in
`Start`), so every client has the tab "Admin overlays" (`game:maplayer-seraphhorizons-admin`). The
server sends `AdminMapPacket {Key, Json}` on channel `seraphhorizons-admin` to a player who switched
an overlay on and holds `controlserver` (checked again at every refresh; one who loses it gets the
overlay removed), every 30 s. Empty `Json` removes the overlay. The payload is a `MapOverlay`
(`Core/AdminMap.cs`): rects, rings and lines in world blocks, marks in pixels, colours as the game's
`ColorUtil.ColorFromRgba` packs them.

Drawing uses only `IRenderAPI.RenderRectangle` (the GUI shader's rectangle outline) at the positions
`GuiElementMap.TranslateWorldPosToViewPos` gives: rings and lines as runs of 2-pixel squares, marks as
nested outlines. No textures, so nothing to load or dispose. It is the minimal layer the issue allowed
for: no icons, no click actions; labels show in the map's hover text.

Overlay providers: `AdminSystem.MapProviders["ore"|"trade"]` builds an overlay for (admin, argument).
The trade overlay takes contributors: `TradingAdminSystem.OverlayContributors.Add((admin, x, z,
radius, overlay) => overlay.Lines.Add(new OverlayLine(...)))` for delivery routes.

## Hooks for later waves

- `TradingAdminSystem.InspectContributors`: `Action<TraderInspection>`, adds lines and JSON fields to
  `/sh trade inspect` (open orders and deliveries at the trader).
- `TradingAdminSystem.OverlayContributors`: the trade overlay (delivery routes).
- `TradingSystem.AdminState`: export sections (orders, deliveries, visitors).
- `AdminLogs.Trade`: the `orders`, `deliveries`, `maps`, `visitors` channels.
- `/sh trade maps` reads the deposit registry directly with #455's rules as written (ore maps from the
  prospector, unsold deposits within 6 km, gravel within 2 km); `TODO(#455)` in
  `TradeAdminCommands.OnMaps` to ask the maps service instead once it is merged.

## Engine facts these rest on (1.22.7)

- `ChatCommandImpl.Execute` runs preconditions, then the node's parsers, then `CallHandler`, which
  dispatches to a subcommand by the next raw word, or checks `GetPrivilege()` (own, else the nearest
  parent's) and runs the handler. A node with parsers parses before its subcommands are looked up.
- `TextCommandCallingArgs.RawArgs` is a public field: the precondition replaces it with the words
  minus `--json`.
- `WaypointMapLayer.ResendWaypoints` is private; `/sh ore markers` adds to `Waypoints` and calls it by
  reflection (as `AddWaypoint` would, without one resend per waypoint).
- Interesting Ore Gen 2.3.8 rolls a hydrothermal district per tile with `LCGRandom` seeded
  `(seed ^ tileX * 1000033, tileZ * 998244353)` (int arithmetic), a district below 0.4, centred at the
  next two draws × the tile size; it builds the district only when a chunk within a tile of it
  generates, and keeps it in memory (`_activeDistricts`), so `/sh ore districts` shows config and
  faults only for districts built since the server started.
