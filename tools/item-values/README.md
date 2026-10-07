# Item values

Every item's base value in rusty gears, derived from the pack's recipe export (#449, part of the
trader overhaul #436). The output, `mods-src/seraphhorizons/assets/seraphhorizons/config/item-values.json`,
ships in the pack's own mod, which serves it to the trading features (`Trading/Values/`, see the
mod's README). Stdlib-only Python 3.11+, like `packtool.py`.

## Commands

```sh
# A recipe export of the pack, with the pack's own mod as this tree has it: from CI (the export
# job's recipe-export artifact), or locally from smoke, which builds mods-src/seraphhorizons and
# loads it in place of any pinned copy (a few minutes; needs `packtool.py fetch` first):
VINTAGE_STORY=$HOME/Games/vintagestory python3 tools/packtool.py smoke --export build/recipes.json
# (Atlas writes one too, but under pack version 0.0.0-test and its own world settings; the table
# CI checks is rebuilt from smoke's export:)
mkdir -p build/atlas-tmp
ITEM_VALUES_EXPORT=$PWD/build/recipes.json TMPDIR=$PWD/build/atlas-tmp VINTAGE_STORY=$HOME/Games/vintagestory \
  dotnet test tests/PackTests --filter "FullyQualifiedName~PackTests.TradingScenarios.Export_is_written_when_asked"

python3 tools/item-values/itemvalues.py build   build/recipes.json   # the table, plus build/item-values-report.md and .json
python3 tools/item-values/itemvalues.py report  build/recipes.json   # the report only, to stdout (--json for JSON)
python3 tools/item-values/itemvalues.py explain build/recipes.json game:pickaxe-tinbronze   # the route, every step's numbers
python3 tools/item-values/itemvalues.py check   build/recipes.json   # CI: the table is current, traders' items have values
python3 -m unittest discover -s tools/tests -p test_item_values.py
```

`check` fails (CI runs it in the export job, on the export smoke dumped) when:

- **The shipped table is stale.** It must equal a rebuild from the export (`values`, `floorZero` and
  `switches`; the `pack` header is not compared). It prints how many codes differ, the first 25 as
  `code: shipped -> rebuilt`, and the command to rebuild. In CI, download the run's
  `recipe-export` artifact and run `build` on it, or run smoke locally; then commit the table.
- **An item traders buy has no value**, derived from the export or in the shipped table. It reads
  the mod's trade lists (`mods-src/seraphhorizons/assets/seraphhorizons/config/tradelists/*.json`,
  vanilla's trade list format; no folder means nothing to check). Traders buy their `buying`
  entries and their `playerSupplied` selling entries (players sell those to them, off the list at
  their value when the buying side does not list them); what a trader only sells is priced by its
  list and needs none. A code counts as valued the way the mod looks it up (`ItemValues.Lookup`):
  directly, or through its variant family's average, or for a code with `*`, the average of what
  it matches. Schematics are exempt: worth nothing by rule, they are bought at their list's price.
- **A trade list names a retired item** (#506): an item of the export that nothing values (no
  route, raw, override or fallback) and the handbook hides, bought or sold, even when its family
  has a value. Take the entry off the list. What the pack removes outright (Hydrate or Diedrate's
  tun, Primitive Survival's irrigation vessels, ppex's gears) is not in the export at all; Immersive
  Woodworking's pit saws and blades are hidden and unvalued. Entries traders only sell whose item
  has no value although the handbook shows it (baby animals, locator maps, found hats) are counted
  and allowed: their lists price them.

It prints how many trade list items traders buy it validated (and how many distinct codes, in how
many lists). It works on an export with or without the pack's own mod: what the mod adds is then
simply absent (but CI's export always has it, and so does the table).

## Rules

The inputs are the export's recipes (every recipe type, each variant a route) and three item
attributes it carries: smelting (with firing and baking), crushing and grinding. The rules are data:

- `raw-values.json`: hand-priced raws. Exact codes, then globs in file order. Ores are priced by
  metal unit (`ores`: an ingot is 100 units, a nugget 5, a chunk by grade the game's
  `metalUnitsByType`), the metal of an ore being what its nugget smelts into. Raws are fixed: no
  recipe changes them. The rusty gear, the unit, is 1.
- `markups.json`: a labour markup per recipe kind (`grid`, `smithing`, `knapping`, `clayforming`,
  `barrel`, `cooking`, `alloy`, `construction`, `transition` for drying/curing/smoking/...,
  `lottery`, `smelting`, `baking`, `crushing`, `grinding`, `mod` for every other mod registry; a mod
  type can have an entry of its own by its type code), the tool fraction, recipe ids never used as
  routes (uncrafting and recycling), how smithing and clay forming use material by volume, and the
  schematic patterns.
- `overrides.json`: hand overrides, fixed and winning over everything, each with its reason.

A route's value per output item is

    (consumed ingredients x (1 + pct) + flat + kept tools x toolFraction - other outputs) / output quantity

floored at zero, where a slot costs its cheapest accepted stack; a grid ingredient counts once per
cell of the pattern; a liquid counts 100 portions a litre; smithing uses filled voxels / 42 ingots
and clay forming filled voxels / 25 clay; an alloy is its inputs at the middle of their ratios;
cooking counts only the ingredients a meal needs (`minQuantity`); and a tool or container not
consumed (`isTool`, a station, a machine's fitted part such as the gear cutter's master: role
`kept` or the record's `machine.kept`, a grid ingredient with consume false, one handed back) adds
`toolFraction` (2%) of its value. A container handed back as something else (a bucket of milk
gives back the bucket) costs the difference. Butchery, perishing and burning
are not routes (one carcass gives a dozen things; hides and meat are raws instead).

**Lotteries.** A `lottery` record (the oiled gear: one ingredient decided by chance into weighted
outcomes, `schema.md`) is a route to each output that can come out. The output's quantity is its
expected items per input, p x q (its outcome's chance times its stack), and every other output is
credited at its value times its own expected items:

    value = (input x (1 + pct) + flat - sum over the other outputs of p_other x q_other x their value) / (p x q)

floored at zero. The `lottery` kind has no labour (pct 0, flat 0), so the oiled gear (1.84, one in
ten a steel gear, else one steel bit at 0.448) prices a steel gear at 10 x 1.84 - 9 x 0.448 = 14.38.
A lottery route waits for the other outputs' values as a route waits for its tools; a loser no
route will ever value is credited 0. Today the gear cutter (10.89) is the cheaper way to a steel
gear, so that is its value, and its `switches` are `GearBlanks` and `GearCutter` (below). The table
is the default config's: it is never rebuilt with a switch off, so with the cutter off the handbook
shows no value for the steel gear, though the lottery would still price it.

**Schematics** (`schematics` in `markups.json`: `*:schematic-*`, `*:*-schematic-*` for BetterRuins,
Abyssal Depths and Scrolled's rolled copies, and Cartwright's `cartschematics-*`; the patterns
cover `config/schematic-gates.json`'s `sold`, which a test holds them to) are kept on crafting and
come only from traders. A slot that takes only schematics is dropped from the route: it adds
nothing and never blocks it (MachineSchematics gates the machines' first stages with
`seraphhorizons:schematic-<machine>`; BetterRuins' recipes keep theirs). A schematic is never
priced: no raw, default or fallback, and no route making one counts.

**Switches.** `item-values.json` has `switches`: per code, the `ModConfig/seraphhorizons.json`
switches (bools on `SeraphHorizonsConfig`) its value exists by, only for codes that have any. An
item depends on switch S when its cheapest route's recipe is owned by S (export
`recipes[i].switch`), when the item itself is added by S (`items[code].switch`), or when anything
that route was priced from (its stacks, tools, handed-back containers and credited outputs)
depends on S, through the whole chain. The mod's handbook hides a value whose switch is off.

Every other item is valued at its cheapest route. The solver settles items cheapest first, each
from items already settled (Knuth's generalisation of Dijkstra), so chains of any length and cycles
resolve, and a settled value never drops again: a loop that makes more than it consumes (two linen
make four sails, a sail cuts back into two linen; a lightning rod chisels into fifty copper bits)
cannot pull prices down. A route waits for its tools and its credited outputs when they will get a
value. When routes wait on each other (a lottery's loser made only from its winner), the cheapest
of them is settled without waiting and the rest wait again. The whole solve runs twice: a route
whose tool is worth more than its output (the gear cutter's frame, 16 gears, cutting an 11-gear
steel gear) could only be priced after that output had settled by a dearer route, so the second
pass takes a tool not yet valued at its first-pass value.

What no recipe makes and nothing prices (a leaf) gets, in this order: a category default by a regex
on the code's path (`defaults` in `raw-values.json`: earth, wood, plants, seeds, crops, meat, hides,
ore, liquids); then, only for what the first pass left unpriced, vanilla's trader price for it (the
mean of its trader entries, sell prices x 0.7, buy prices x 1.4), else the average of its siblings,
the valued codes that differ from it in one variant segment (a black-glazed mold takes the average
of the blue and red ones; planks facing north take planks facing up). These fallbacks never
undercut a production chain: they are settled after it.

Values are stored per item as gears with 3 decimals. An item worth under 1 gear per full stack is
listed in `floorZero`: trading treats it as worthless, and keeps the value for sums. The table's
keys are `about`, `schemaVersion`, `pack`, `values`, `floorZero`, `switches`, one entry per line in
code order, so a rebuild diffs cleanly.

The report (`build/item-values-report.md` and `.json`) lists coverage per mod domain, the items with
no value, the 50 most and least valuable, and items valued below the ingredients of their route,
which only a raw or override can be: the review list for hand prices.

## Numbers

From smoke's export of the pack (pack 1.1.0, game 1.22.7, with mods-src/seraphhorizons loaded;
27,163 items, 10,517 recipes), the export as of the item-values branch at 98db1fb (main merged):

- 21,918 of 27,163 items valued (80.7%); of the 24,023 the handbook shows, 84.5%.
- 13,773 from recipes, 5,797 raws, 2,330 defaults and fallbacks, 18 overrides.
- 2,044 worthless (under a gear per stack), 9.3% of those valued.
- 107 valued below their ingredients, all raws (nuggets, which the game hammers from ore chunks of
  more units; boards, wool) and the overrides: by design.
- No value: 5,245, mostly things no player trades: creatures, loose surface ores, plant and crop
  blocks, rich gravel, coral, butterflies, termite mounds, stalagmites, carcasses (butchery is
  skipped), technical blocks, the schematics (87, by rule), and the retired pit saws and blades.
  Expanded Foods' sausages (72) lost theirs with main's DuplicateRecipes: the one sausage recipe
  left is Butchering's kneading, which takes offal, and offal (a butchery output) has no value.
- Coverage per domain: game 77.5%, Expanded Foods 94.9%, Door Variants 100%, Tailor's Delight
  100%, Alchemy 98.0%, Cartwright's 98.6%, ppex 100%, Butchering 20.4% (carcasses),
  seraphhorizons 71.2% (its schematics, maps and leads have none).

Samples (gears per item; vanilla trader prices per item for reference, sell / buy):

| item | value | stack | vanilla | route |
|---|---|---|---|---|
| copper ingot | 2.07 | 16 | — / 1 | 20 nuggets smelted |
| tin bronze pickaxe | 4.37 | 1 | 11 / 4 | smithed head + stick |
| bread (spelt) | 0.175 | 32 | 0.25 / 0.125 | vanilla trader fallback |
| linen | 1.0 | 64 | 3 / 0.5 | grid |
| board (oak) | 0.06 | 64 | 0.0625 / — | raw |
| glass | 0.16 | 64 | 0.25 / 0.125 | crushed quartz smelted |
| leather | 0.54 | 64 | — | tanned in a barrel, 2 per medium hide |
| rusty gear | 1 | 1000 | | raw (money) |
| bed (wood) | 1.19 | 2 | 8 / — | grid |
| barrel | 1.5 | 1 | 2 / — | override |
| oiled gear | 1.84 | 64 | | rusty gear degreased, pickled, neutralized, oiled |
| steel gear | 10.89 | 64 | | gear cutter (a cast steel blank, 8.87); 14.38 by the oiled gear's lottery |

At commit 31052e6, over the 475 items vanilla traders deal in, the median value is 1.2 x vanilla's buy price (266
entries) and 0.7 x its sell price (346): values sit near what a trader pays, below what it charges.

## Why these prices

- **Reference.** Vanilla trade lists (`assets/survival/config/tradelists/`), where traders buy at
  about half what they sell for. A value is the item's worth on the table's scale, calibrated to
  what a vanilla trader that wants the item pays (#436), so the target is vanilla's buy price, or
  between buy and sell where vanilla only sells. What the pack's traders actually pay is a fifth of the value
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
  plus labour). No gear is overridden: the steel gear takes its cheapest route (the gear cutter,
  or the oiled gear's lottery, Lotteries above), and the large steel gear its gear cutter route.
- **Schematics** have no value: they are kept on crafting, and traders are their only source, at
  their lists' prices (the curio dealer, which also buys back Abyssal Depths' diving gear schematic
  at its list's price, the mechanic, the smith, ...). A machine built with one is worth its parts
  and labour.

## Known gaps

- The pack's woodworking machines, kiln glazing, bread baking stages and the cementation furnace are
  not in the export; items made only that way are raws, overrides, or take a fallback.
- A pickling tub record's `failure` output (gears an acid eats, brine's `lossChance`) is not
  credited or charged: the tub prices its first output as if no gear were lost. Perishing (the bare
  gears' flash rust) is not a route.
- A nugget hammered from a rich chunk carries more metal units in its attributes than the plain
  nugget the table prices; trading reads the code, not the attributes.
- Values are per item; a stack's attributes (a filled bucket's contents, a meal's ingredients) are
  not priced.
