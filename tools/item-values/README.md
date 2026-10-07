# Item values

Every item's base value in rusty gears, derived from the pack's recipe export (#449, part of the
trader overhaul #436). The output, `mods-src/seraphhorizons/assets/seraphhorizons/config/item-values.json`,
ships in the pack's own mod, which serves it to the trading features (`Trading/Values/`, see the
mod's README). Stdlib-only Python 3.11+, like `packtool.py`.

## Commands

```sh
# A recipe export of the pack: from CI (the export job's recipe-export artifact, or a release's
# recipes.json), or locally through Atlas (writes it in a minute or two):
mkdir -p build/atlas-tmp
ITEM_VALUES_EXPORT=$PWD/build/recipes.json TMPDIR=$PWD/build/atlas-tmp VINTAGE_STORY=$HOME/Games/vintagestory \
  dotnet test tests/PackTests --filter "FullyQualifiedName~PackTests.TradingScenarios.Export_is_written_when_asked"

python3 tools/item-values/itemvalues.py build   build/recipes.json   # the table, plus build/item-values-report.md and .json
python3 tools/item-values/itemvalues.py report  build/recipes.json   # the report only, to stdout (--json for JSON)
python3 tools/item-values/itemvalues.py explain build/recipes.json game:pickaxe-tinbronze   # the route, every step's numbers
python3 tools/item-values/itemvalues.py check   build/recipes.json   # CI: every item traders buy has a value
python3 -m unittest discover -s tools/tests -p test_item_values.py
```

`check` reads the mod's trade lists (`mods-src/seraphhorizons/assets/seraphhorizons/config/tradelists/*.json`,
vanilla's trade list format; no folder means nothing to check) and fails when an item traders buy
has no value, either derived from the export or in the shipped table. Traders buy their `buying`
entries and their `playerSupplied` selling entries (players sell those to them, off the list at
their value when the buying side does not list them); what a trader only sells is priced by its list
and needs none. A code counts as valued the way the mod looks it up (`ItemValues.Lookup`): directly,
or through its variant family's average, or for a code with `*`, the average of what it matches. It
warns, without failing, when the shipped table differs from what the export derives: rebuild and
commit the table when a pack change matters. It prints how many trade list items it validated (and
how many distinct codes, in how many lists). It works on an export with or without the pack's own
mod: what the mod adds is then simply absent. CI runs it in the export job.

## Rules

The inputs are the export's recipes (every recipe type, each variant a route) and three item
attributes it carries: smelting (with firing and baking), crushing and grinding. The rules are data:

- `raw-values.json`: hand-priced raws. Exact codes, then globs in file order. Ores are priced by
  metal unit (`ores`: an ingot is 100 units, a nugget 5, a chunk by grade the game's
  `metalUnitsByType`), the metal of an ore being what its nugget smelts into. Raws are fixed: no
  recipe changes them.
- `markups.json`: a labour markup per recipe kind (`grid`, `smithing`, `knapping`, `clayforming`,
  `barrel`, `cooking`, `alloy`, `construction`, `transition` for drying/curing/smoking/...,
  `smelting`, `baking`, `crushing`, `grinding`, `mod` for every other mod registry; a mod type can
  have an entry of its own by its type code), the tool fraction, recipe ids never used as routes
  (uncrafting and recycling), and how smithing and clay forming use material by volume.
- `overrides.json`: hand overrides, fixed and winning over everything, each with its reason.
- Schematics (#506): every code matched by a `sold` pattern of the mod's
  `config/schematic-gates.json` (`--gates`) is valued from the pack's trade lists (`--tradelists`)
  by traderFallback's rule: the mean over its entries of the price per item, selling x 0.7 and
  buying / 0.2 x 1.4 (x 7: the lists' buying prices are the final pay, value x the mod's buy spread,
  `BUY_SPREAD`, `BuySpread`'s default). Fixed like a raw (an override still wins); a schematic no list prices takes the
  fallbacks below. No data file of their own: the trade lists' hand prices are the values.

A route's value per output item is

    (consumed ingredients x (1 + pct) + flat + kept tools x toolFraction) / output quantity

where a slot costs its cheapest accepted stack; a grid ingredient counts once per cell of the
pattern; a liquid counts 100 portions a litre; smithing uses filled voxels / 42 ingots and clay
forming filled voxels / 25 clay; an alloy is its inputs at the middle of their ratios; cooking
counts only the ingredients a meal needs (`minQuantity`); and a tool or container not consumed
(`isTool`, a station, a machine's `kept` part, an ingredient with consume false, one handed back)
adds `toolFraction` (2%) of its value. A container handed back as something else (a bucket of milk
gives back the bucket) costs the difference. A kept schematic (an ingredient not consumed whose
codes all match the gates' `sold` patterns: MachineSchematics' machine gates, and every recipe that
uses a schematic) adds nothing, not even the tool fraction, and needs no value: a gated machine is
priced by its consumed parts and labour only. Other kept tools (hammers, saws) keep the fraction.
Butchery, perishing and burning are not routes (one carcass gives a dozen things; hides and meat
are raws instead).

Every other item is valued at its cheapest route. The solver settles items cheapest first, each
from items already settled (Knuth's generalisation of Dijkstra), so chains of any length and cycles
resolve, and a settled value never drops again: a loop that makes more than it consumes (two linen
make four sails, a sail cuts back into two linen; a lightning rod chisels into fifty copper bits)
cannot pull prices down. A route waits for its tools when they will get a value.

What no recipe makes and nothing prices (a leaf) gets, in this order: a category default by a regex
on the code's path (`defaults` in `raw-values.json`: earth, wood, plants, seeds, crops, meat, hides,
ore, liquids); then, only for what the first pass left unpriced, vanilla's trader price for it (the
mean of its trader entries, sell prices x 0.7, buy prices x 1.4), else the average of its siblings,
the valued codes that differ from it in one variant segment (a black-glazed mold takes the average
of the blue and red ones; planks facing north take planks facing up). These fallbacks never
undercut a production chain: they are settled after it.

Values are stored per item as gears with 3 decimals. An item worth under 1 gear per full stack is
listed in `floorZero`: trading treats it as worthless, and keeps the value for sums.

The report (`build/item-values-report.md` and `.json`) lists coverage per mod domain, the items with
no value, the 50 most and least valuable, and items valued below the ingredients of their route,
which only a raw or override can be: the review list for hand prices.

## Numbers

From an Atlas export of the pack at commit 31052e6 (game 1.22.7, 26,708 items, 11,053 recipes):

- 21,113 of 26,708 items valued (79.0%); of the 23,569 the handbook shows, 82.6%.
- 13,232 from recipes, 5,795 raws, 2,078 defaults and fallbacks, 8 overrides.
- 1,892 worthless (under a gear per stack), 9.0% of those valued.
- 101 valued below their ingredients, all raws (nuggets, which the game hammers from ore chunks of
  more units; boards, wool) and the overrides: by design.
- No value: 5,595, mostly things no player trades: creatures (463), loose surface ores (644), plant
  and crop blocks, rich gravel, coral, butterflies, termite mounds, stalagmites, carcasses
  (butchery is skipped), technical blocks (signals' resistors, More Roads' stairs).
- Coverage per domain: game 74.5%, Expanded Foods 98.2%, Door Variants 100%, Tailor's Delight
  100%, Alchemy 99.6%, Cartwright's 99.0%, ppex 100%, Butchering 20.4% (carcasses).

Samples (gears per item; vanilla trader prices per item for reference, sell / buy):

| item | value | stack | vanilla | route |
|---|---|---|---|---|
| copper ingot | 2.07 | 16 | — / 1 | 20 nuggets smelted |
| tin bronze pickaxe | 4.59 | 1 | 11 / 4 | smithed head + stick |
| bread (spelt) | 0.175 | 32 | 0.25 / 0.125 | vanilla trader fallback |
| linen | 1.0 | 64 | 3 / 0.5 | grid |
| board (oak) | 0.06 | 64 | 0.0625 / — | raw |
| glass | 0.16 | 64 | 0.25 / 0.125 | crushed quartz smelted |
| leather | 0.54 | 64 | — | tanned in a barrel, 2 per medium hide |
| rusty gear | 1 | 1000 | | raw (money) |
| bed (wood) | 1.19 | 2 | 8 / — | grid |
| barrel | 1.5 | 1 | 2 / — | override |

Over the 475 items vanilla traders deal in, the median value is 1.2 x vanilla's buy price (266
entries) and 0.7 x its sell price (346): values sit near what a trader pays, below what it charges.

## Why these prices

- **Reference.** Vanilla trade lists (`assets/survival/config/tradelists/`), where traders buy at
  about half what they sell for. A value is the item's worth on the table's scale, calibrated to what a vanilla trader
  that wants the item pays (#436), so the target is vanilla's buy price, or between buy and sell
  where vanilla only sells. What the pack's traders actually pay is a fifth of the value
  (`BuySpread`, #506); what they ask is the value.
- **Metal.** Vanilla sells 16 copper nuggets for 2 gears (0.125 each, 2.5 an ingot's worth) and buys
  a copper ingot for 1. Copper is 0.017 a unit: a nugget 0.085, an ingot 2.07 after smelting
  (+10%, +0.2 fuel). Vanilla buys tin at 2x copper, silver 3x, gold 4x; tin 0.035 (ingot 4.05), zinc
  and bismuth 0.03, lead 0.015, silver 0.06 (6.8), gold 0.08 (9), nickel 0.05, chromium 0.07,
  titanium 0.08, platinum 0.2. Iron comes from a bloom forged on the anvil (ingot 3.5); blister
  steel, made in the cementation furnace the export does not carry, is a raw at 5 (steel ingot 8).
- **Smithing** carries the most labour (+30% and 1.5 gears a piece): a tin bronze pickaxe head is
  0.57 ingot and lands at 4.6 with the stick, against vanilla's 4 to buy and 11 to sell.
- **Wood.** Boards are raws at vanilla's 0.0625: the pack saws them with Immersive Woodworking, whose
  recipes the export does not carry. Logs 0.22, debarked 0.24, support beams 0.3.
- **Hides and leather.** A medium hide is 2 (hunting is risky), small 1, large 3.5, huge 6; tanning
  takes days in barrels (+5%, 0.1 a batch), so leather ends at about 0.54.
- **Earth and plants** are near worthless on purpose (stone 0.008, sand and gravel 0.01, clay 0.03):
  a full stack is under a gear, so trading ignores them.
- **Crops and seeds.** Grain 0.03, vegetables 0.04, seeds 0.1, tree seeds 0.25 (vanilla sells exotic
  tree seeds for 8: those are a trader's margin, not a value).
- **Gems.** Rough gems at vanilla's prices (diamond and emerald about 10, olivine 5, garnet 2.5).
- **Overrides.** The barrel (3 boards and 4 sticks make it 0.34, a cooper's work is worth more;
  vanilla sells it for 2) and the anvils (cast in molds, which the export does not carry: 9 ingots
  plus labour). The steel gear is 15, the reclamation line's cost: one oiled gear in ten rolls sound
  and the rest become a steel bit each, and an oiled gear costs 1.83 by these markups (a rusty gear
  and its lye, acid, lime water and lard), so ten of them, 18.26, less nine bits at 0.4 (`why` has
  the steps). The roll is a lottery the solver cannot weigh. The large steel gear takes its gear
  cutter route (the smithed blank, the oil, labour).
- **Schematics** are what the pack's traders ask for them (x 0.7; a buying entry counts / 0.2 x 1.4), so a schematic trades back near
  70% of its price; a machine built with one is worth its parts, as the schematic is kept.

## Known gaps

- Casting in tool molds, the pack's woodworking machines, kiln glazing, bread baking stages and the
  cementation furnace are not in the export; items made only that way are raws, overrides, or take
  a fallback.
- A nugget hammered from a rich chunk carries more metal units in its attributes than the plain
  nugget the table prices; trading reads the code, not the attributes.
- Values are per item; a stack's attributes (a filled bucket's contents, a meal's ingredients) are
  not priced.
