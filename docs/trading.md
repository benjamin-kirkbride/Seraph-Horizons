# Traders

The trader overhaul (epic #436), built in the pack's own mod (`mods-src/seraphhorizons/Trading/`).
This note starts with the spike (#446): where the game lets us in, how other mods add traders and
trades, what we found blocking or awkward. Then what the first wave built on it (#447 camps on a
grid, #448 eleven trader types with curated regional stock), with the extension points later waves
use. Later waves extend this file.

Game 1.22.7; decompiled sources of `VSSurvivalMod` (`Vintagestory.GameContent`) and `VSEssentials`
(`Vintagestory.ServerMods`).

## How the game does traders

### Entity, stock and restock

- **Entity**: `entities/humanoid/trader-{male,female}.json`, code `trader`, variants
  `gender`-`type`-`climate` (nine types; `cold`, `temperate`, `desert` only pick outfits), class
  `EntityTrader` → `EntityTradingHumanoid` → `EntityDressedHumanoid`. Outfits come from
  `partialRandomOutfitsByType` (keys `trader-male-luxuries-*` and `trader-male-*-{climate}`) and
  `config/traderaccessories-{gender}`; the name from `nametag.selectFromRandomName`; dialogue from the
  `conversable` behavior (`config/dialogue/trader`, `…/treasurehunter`). Asset paths in an entity's
  attributes resolve in the entity's own domain (`outfitConfigFileName`, the dialogue path,
  `tradePropsFile`), so a mod's copy has to say `game:` for the game's.
- **Trade list**: `EntityTradingHumanoid.Initialize` (server) reads `TradeProps`
  (`TradeProperties { NatFloat Money; TradeList Buying, Selling }`, `TradeList { MaxItems; TradeItem[] List }`)
  from the attribute `tradePropsFile` (`config/tradelists/trader-{type}`, deserialised with
  Newtonsoft) or inline `tradeProps`. `TradeItem` is a `JsonItemStack` plus `Price`, `Stock` (NatFloat),
  `Restock`, `SupplyDemand` (both unused by the game) and `AttributesToIgnore` (attributes whose value is
  `"*"`). `TradeItem.Resolve(world)` gives a `ResolvedTradeItem` (stack, rolled price and stock); it
  appends to `AttributesToIgnore` every call, so one `TradeItem` must not be resolved repeatedly.
- **Restock**: `RefreshBuyingSellingInventory(refreshChance)` (protected, not virtual) shuffles each
  list, takes up to `MaxItems` resolvable entries not already on the shelf, and replaces each slot with
  chance `refreshChance`. Called with 1.1 (everything) from `reloadTradingList` (private: on spawn,
  and on load after a schematic import), and weekly with 0.5 from `OnGameTick`
  (`doubleRefreshIntervalDays = 7`, tracked in `WatchedAttributes["lastRefreshTotalDays"]`), which
  also tops the wallet up towards `TradeProps.Money` (+7–28 % of it, at most −3 below). A collectible
  implementing `ITradeableCollectible.ShouldTrade` can refuse to be stocked (locator maps).
- **Inventory**: `InventoryTrader`, 41 slots: 16 selling (0–15), 4 buying cart (16–19), 16 buying
  (20–35), 4 selling cart (36–39), money (40). Saved in `WatchedAttributes["traderInventory"]`
  (`ResolvedTradeItem.ToTreeAttributes`: stack, price, stock, …; nothing else fits in there).
- **A deal** (`InventoryTrader.TryBuySell`, internal; packet 1000 from the dialog): the player's money
  (`GetPlayerAssets`), the trader's (`GetTraderAssets`, rusty gears in slot 40),
  `HasTraderEnoughStock`, `HasTraderEnoughDemand`, `HasTraderEnoughAssets`. What the trader buys is
  decided by `IsTraderInterestedIn` → `GetBuyingConditionsSlot(stack)`: a buying slot whose stack
  equals or `Satisfies` the offered one, and fresh enough. Prices are the slot's
  `ResolvedTradeItem.Price`, per stack size.
- **Dialog**: `GuiDialogTrader(InventoryTrader, EntityAgent, capi)`, opened by the dialogue trigger
  `opentrade` (`Dialog_DialogTriggers`, protected virtual). Two tabs (trade, and the auction house),
  search and price filters, all private; it redraws from the inventory (`TraderInventory_SlotModified`).
  Anything we add to it (prices, standing, orders) is a Harmony postfix on `Compose`, or a dialog of
  our own opened from a dialogue component; the dialogue system (`EntityBehaviorConversable`,
  `DialogueController.DialogTriggers`) is the clean place for orders and deliveries.
- **Handbook**: `TradeHandbookInfo` builds "sold by / bought by" from each entity type's
  `tradePropsFile`/`tradeProps`, as does the recipe exporter (`tools/recipe-export/Items/SourceIndex.cs`).
  It runs at the client's `LevelFinalize` and adds one trader's name to one item's section in the
  private `AddTraderHandbookInfo(TradeItem, string traderName, string title)`, the name being
  `Lang.GetMatching("{domain}:item-creature-{path}")`. Our types have neither (see open problems).
  In a grid world the traders the grid replaces are skipped there (`Trading/Game/TraderHandbook.cs`).
- **Story schematics**: `WorldGenStoryStructure.schematicData` (internal, VSEssentials) is loaded at
  `GenStoryStructures.InitWorldGen` for every story structure, and not at all without lore content
  (`scfg` stays null). A spawner's block entity data decodes (`BlockSchematic.DecodeBlockEntityData`)
  to a tree with `entityCodes`. Measured 2026-10-08 in a survival world: vanilla's treasure hunter
  spawns `game:trader-{male,female}-treasurehunter-temperate`; BetterRuins' story ruins name
  `game:humanoid-trader-{clothing,foods,furniture,treasurehunter}`, codes no entity type has in 1.22.

### Death and respawn

- Traders have `reviveondeath` (24–72 h) and `health` 25: a killed trader lies dead and revives where
  it died; `EntityTrader.Revive` moves it back to `Attributes["spawnX/Y/Z"]` if set (spawners don't set
  it). Its stock and wallet survive (same entity).
- Camp traders come from a **`meta-spawner`** block in the camp schematic (`BlockEntitySpawner`,
  `BESpawnerData`: `entityCodes` such as `game:trader-female-agriculture-temperate&game:trader-male-…`,
  `maxCount` 1, interval hours, `internalCapacity`). Every 2 s it fires the event bus event
  **`onattemptspawnerspawn`** with a tree `{ type, pos }` that listeners may rewrite; it spawns again
  only when its entity is gone (despawned or unloaded dead). So a camp outlives its trader.
- `ModSystemClimateSpecificTraderTypes` is that event's vanilla listener: `-temperate` →
  `-desert`/`-cold` by climate (it tests `StartsWith("trader")` on a `domain:path` string, so it does
  not fire for `game:` codes as spawners give them).

### Camps in worldgen

- `GenStructures` (VSEssentials, ExecuteOrder 0.3) loads every domain's `worldgen/structures.json` into
  `scfg` (internal `WorldGenStructuresConfig`, `Structures` array) at `InitWorldGenerator`. Per chunk
  column (pass `TerrainFeatures`; `postPass: true` structures from `GenStructuresPosPass`, 0.5, same
  pass) it shuffles the structures and rolls each `chance × chanceMultiplier` (0.46) at random spots in
  the chunk, calling `WorldGenStructure.TryGenerate` (internal): climate/forest/height filters, then
  the placement generator, which checks `SatisfiesMinDistance` (no generated structure of the same
  `group` within `minGroupDistance`, searched in the map regions around) and overlap. A placed one is
  recorded in the map region (`GeneratedStructure { Code, Group, Location }`) and, if
  `buildProtected`, gets a land claim.
- Trader camps: group `trader`, `minGroupDistance` 600: vanilla's forest, shallow, hotdry, repurposed,
  cold, plains, outpost (two or three traders), treehouse; **BetterTraders 0.2.1** adds five structures
  of its own in the same group (`trader-plains`, `-outpost`, `-coldclimate`, `-wall`, `-fort`, chance
  0.1–0.12, each schematic one spawner with vanilla codes); **Domestic Animal Trader 1.1.5** a
  `tradercavaran` structure (chance 0.04) with a spawner of `game:humanoid-trader-domesticanimal`, adds
  the variant `domesticanimal` to vanilla's trader entities, its own list
  (`game:config/tradelists/trader-domesticanimal.json`) and outfits; **Culinary Artillery** a wagon
  (group `trader`, chance 0.2) and a second near spawn (group `acatraderclosetospawn`), with its own
  entity `aculinaryartillery:trader-{gender}-kitchenware-{climate}` and list.
- No trader list or entity is ever missing in the pack: other mods' patches to vanilla's lists keep
  applying to vanilla's files, which stay; nothing of ours removes them, so the patch loader has
  nothing to report.

### Notice Board

Not in the pack (`noticeboard` 3.0.x, needs Attribute Rendering Library), so not examined here;
the deferred settlements (#468) look at what it exposes then.

## The pack's trade entries in vanilla lists

Eleven pack mods add to vanilla's trader lists (and two bring traders of their own). Every entry,
with the pack's types that took it in, is in
`mods-src/seraphhorizons/tests/Trading/fixtures/curation-sources.json` (1224 rows, vanilla's own nine
lists included), which `ShippedTradeListsTests` holds the shipped lists to. Entries naming mods the
pack does not have (Domestic Animal Trader's cats, pets, dogs, yak and aurochs, Primitive Survival
fish mounts, Culinary Artillery's bottle racks of other mods' woods) are marked "not in the pack".

| Mod | Entries | Into vanilla's |
|---|---|---|
| Culinary Artillery 2.0.0-dev.26 | 3 + its own list (108 sell, 108 buy, plus compat variants) | clothing (sell) |
| Oils Resoaped 1.0.3 | 35 | agriculture, artisan, clothing (buy); commodities, luxuries (sell) |
| BetterRuins 0.6.4 | 3 (+ dialogue quests on luxuries and agriculture traders) | treasurehunter (sell) |
| Domestic Animal Trader 1.1.5 | its own list (58 sell, 47 buy) | — |
| Craftable Locusts | 2 | treasurehunter (buy, sell) |
| Tailor's Delight 2.2.2 | 76 | artisan, clothing, commodities, furniture, luxuries, survivalgoods, treasurehunter |
| Wool 1.9.6 | 49 (+ commodities `maxItems` 10) | artisan, clothing, commodities, survivalgoods, treasurehunter |
| Signals 0.3.3 | 5 | commodities, treasurehunter (sell) |
| Butchering 1.14.3 | 4 | luxuries, survivalgoods, treasurehunter (buy) |
| Adventurer's Walking Stick 3.0.10 | 2 | luxuries, treasurehunter (sell) |
| Abyssal Depths 1.0.20 | 2 | treasurehunter (buy, sell) |
| Translocator Map | 1 | treasurehunter (sell) |

The issue's counts (Culinary Artillery 42, BetterRuins 21, …) counted patch operations; the table
counts entries.

Every entry, and where it went (`type side`):

### Culinary Artillery 2.0.0-dev.26

| Vanilla list | Side | Item | Ours |
|---|---|---|---|
| clothing | sell | `aculinaryartillery:clothes-head-c-hat` | tailor sell, cook sell |
| clothing | sell | `aculinaryartillery:clothes-head-c-hat1` | tailor sell, cook sell |
| clothing | sell | `aculinaryartillery:clothes-head-c-hat2` | tailor sell, cook sell |
Its own trader's list (trader-kitchenware, 362 entries), by kind:

| Kind | Side | Entries | Ours |
|---|---|---|---|
| `aculinaryartillery:bottlerack` | sell | 114 | carpenter sell, cook sell, not in the pack |
| `aculinaryartillery:bottlerack` | buy | 114 | carpenter sell, cook buy, cook buy (as its kind), not in the pack |
| `aculinaryartillery:saucepan` | sell | 9 | cook sell |
| `aculinaryartillery:cauldron` | sell | 7 | cook sell |
| `aculinaryartillery:cauldronmini` | sell | 7 | cook sell |
| `aculinaryartillery:bottle-clay` | sell | 9 | cook sell |
| `aculinaryartillery:bottle-glass` | sell | 11 | cook sell |
| `aculinaryartillery:cork-generic` | sell | 1 | cook sell |
| `aculinaryartillery:spile` | sell | 7 | cook sell |
| `aculinaryartillery:mixingbowl` | sell | 9 | cook sell |
| `aculinaryartillery:poweredmixingbowl` | sell | 9 | cook sell |
| `aculinaryartillery:meathooks` | sell | 1 | cook sell |
| `aculinaryartillery:saucepan` | buy | 9 | cook buy, cook buy (as its kind) |
| `aculinaryartillery:cauldron` | buy | 4 | cook buy, cook buy (as its kind) |
| `aculinaryartillery:cauldronmini` | buy | 4 | cook buy, cook buy (as its kind) |
| `aculinaryartillery:bottle-clay` | buy | 9 | cook buy, cook buy (as its kind) |
| `aculinaryartillery:bottle-glass` | buy | 11 | cook buy, cook buy (as its kind) |
| `aculinaryartillery:cork-generic` | buy | 1 | cook buy |
| `aculinaryartillery:spile` | buy | 7 | cook buy, cook buy (as its kind) |
| `aculinaryartillery:mixingbowl` | buy | 9 | cook buy, cook buy (as its kind) |
| `aculinaryartillery:poweredmixingbowl` | buy | 9 | cook buy, cook buy (as its kind) |
| `aculinaryartillery:meathooks` | buy | 1 | cook buy |

### Oils Resoaped 1.0.3

| Vanilla list | Side | Item | Ours |
|---|---|---|---|
| agriculture | buy | `oils:soap-wood-flax` | generalstore sell, farmer buy |
| agriculture | buy | `oils:soap-wood-olive` | generalstore sell, farmer buy |
| agriculture | buy | `oils:soap-wood-peanut` | generalstore sell, farmer buy |
| agriculture | buy | `oils:soap-wood-rice` | generalstore sell, farmer buy |
| agriculture | buy | `oils:soap-wood-soy` | generalstore sell, farmer buy |
| agriculture | buy | `oils:soap-wood-sunflower` | generalstore sell, farmer buy |
| agriculture | buy | `oils:soap-wood-walnut` | generalstore sell, farmer buy |
| artisan | buy | `oils:soap-potash-flax` | generalstore sell, mason buy |
| artisan | buy | `oils:soap-potash-olive` | generalstore sell, mason buy |
| artisan | buy | `oils:soap-potash-peanut` | generalstore sell, mason buy |
| artisan | buy | `oils:soap-potash-rice` | generalstore sell, mason buy |
| artisan | buy | `oils:soap-potash-soy` | generalstore sell, mason buy |
| artisan | buy | `oils:soap-potash-sunflower` | generalstore sell, mason buy |
| artisan | buy | `oils:soap-potash-walnut` | generalstore sell, mason buy |
| clothing | buy | `oils:soap-pure-flax` | tailor buy, curiodealer sell |
| clothing | buy | `oils:soap-pure-olive` | tailor buy, curiodealer sell |
| clothing | buy | `oils:soap-pure-peanut` | tailor buy, curiodealer sell |
| clothing | buy | `oils:soap-pure-rice` | tailor buy, curiodealer sell |
| clothing | buy | `oils:soap-pure-soy` | tailor buy, curiodealer sell |
| clothing | buy | `oils:soap-pure-sunflower` | tailor buy, curiodealer sell |
| clothing | buy | `oils:soap-pure-walnut` | tailor buy, curiodealer sell |
| commodities | sell | `oils:soap-wood-flax` | generalstore sell, farmer buy |
| commodities | sell | `oils:soap-wood-olive` | generalstore sell, farmer buy |
| commodities | sell | `oils:soap-wood-peanut` | generalstore sell, farmer buy |
| commodities | sell | `oils:soap-wood-rice` | generalstore sell, farmer buy |
| commodities | sell | `oils:soap-wood-soy` | generalstore sell, farmer buy |
| commodities | sell | `oils:soap-wood-sunflower` | generalstore sell, farmer buy |
| commodities | sell | `oils:soap-wood-walnut` | generalstore sell, farmer buy |
| luxuries | sell | `oils:soap-pure-flax` | tailor sell, curiodealer sell |
| luxuries | sell | `oils:soap-pure-olive` | tailor sell, curiodealer sell |
| luxuries | sell | `oils:soap-pure-peanut` | tailor sell, curiodealer sell |
| luxuries | sell | `oils:soap-pure-rice` | tailor sell, curiodealer sell |
| luxuries | sell | `oils:soap-pure-soy` | tailor sell, curiodealer sell |
| luxuries | sell | `oils:soap-pure-sunflower` | tailor sell, curiodealer sell |
| luxuries | sell | `oils:soap-pure-walnut` | tailor sell, curiodealer sell |

### BetterRuins 0.6.4

| Vanilla list | Side | Item | Ours |
|---|---|---|---|
| treasurehunter | sell | `betterruins:locatormap-undergroundruins` | curiodealer sell |
| treasurehunter | sell | `betterruins:locatormap-mine1` | curiodealer sell |
| treasurehunter | sell | `betterruins:locatormap-mine2` | curiodealer sell |

### Domestic Animal Trader 1.1.5

Its own trader's list (trader-domesticanimal, 101 entries), by kind:

| Kind | Side | Entries | Ours |
|---|---|---|---|
| `creature-sheep-bighorn-baby-male` | sell | 1 | animaldealer sell |
| `creature-sheep-bighorn-baby-female` | sell | 1 | animaldealer sell |
| `feather` | sell | 1 | animaldealer sell, generalstore sell |
| `creature-hyena-spotted-baby-*` | sell | 1 | animaldealer sell (each variant) |
| `creature-tameddeer-elk-male-adult` | sell | 1 | animaldealer sell |
| `creature-tameddeer-elk-female-adult` | sell | 1 | animaldealer sell |
| `primitivesurvival:fireflies-treetop` | sell | 1 | animaldealer sell |
| `primitivesurvival:fireflies-mysticlantern` | sell | 1 | animaldealer sell |
| `primitivesurvival:fireflies-blueghost` | sell | 1 | animaldealer sell |
| `primitivesurvival:fireflies-rover` | sell | 1 | animaldealer sell |
| `primitivesurvival:fireflies-fairyring` | sell | 1 | animaldealer sell |
| `primitivesurvival:fireflies-candle` | sell | 1 | animaldealer sell |
| `primitivesurvival:fireflies-marshimp` | sell | 1 | animaldealer sell |
| `primitivesurvival:snake-blackrat` | sell | 1 | animaldealer sell |
| `primitivesurvival:snake-coachwhip` | sell | 1 | animaldealer sell |
| `primitivesurvival:snake-pitviper` | sell | 1 | animaldealer sell |
| `primitivesurvival:snake-chainviper` | sell | 1 | animaldealer sell |
| `creature-locust-corrupt-hacked` | sell | 1 | curiodealer sell, mechanic buy |
| `creature-locust-bronze-hacked` | sell | 1 | curiodealer sell, mechanic buy |
| `creature-bell` | sell | 1 | curiodealer sell |
| `primitivesurvival:livingdead` | sell | 1 | animaldealer sell |
| `creature-chicken-baby` | sell | 1 | animaldealer sell |
| `creature-pig-wild-piglet` | sell | 1 | not in the pack |
| `wolftaming:creature-dog-wolf-pup` | sell | 1 | not in the pack |
| `creature-hare-european-baby-male` | sell | 1 | animaldealer sell |
| `creature-hare-european-baby-female` | sell | 1 | animaldealer sell |
| `creature-raccoon-common-baby-*` | sell | 1 | animaldealer sell (each variant) |
| `hay-ud` | sell | 2 | animaldealer sell, farmer sell |
| `cats:creature-cat-kitten-ocelot` | sell | 1 | not in the pack |
| `cats:creature-cat-kitten-european` | sell | 1 | not in the pack |
| `cats:creature-cat-kitten-serval` | sell | 1 | not in the pack |
| `cats:creature-cat-kitten-house` | sell | 1 | not in the pack |
| `wolftaming:creature-dog-hunting-pup` | sell | 1 | not in the pack |
| `wolftaming:creature-dog-shepherd-pup` | sell | 1 | not in the pack |
| `wolftaming:creature-dog-corgi-pup` | sell | 1 | not in the pack |
| `petai:backpack-empty` | sell | 1 | not in the pack |
| `petai:petwhistle` | sell | 1 | not in the pack |
| `petai:petcookie-veggie` | sell | 1 | not in the pack |
| `petai:petcookie-meat` | sell | 1 | not in the pack |
| `petai:petnest` | sell | 10 | not in the pack |
| `petai:petnest-purple` | sell | 1 | not in the pack |
| `petai:petnest-white` | sell | 1 | not in the pack |
| `bone` | sell | 1 | animaldealer sell, farmer buy |
| `medievalexpansion:creature-aurochs-lamb` | sell | 1 | not in the pack |
| `thecritterpack:creature-yak-calf` | sell | 1 | not in the pack |
| `petai:petcookie-veggie` | buy | 1 | not in the pack |
| `feather` | buy | 1 | animaldealer buy, generalstore buy |
| `primitivesurvival:smokedmeat-fish-raw` | buy | 1 | animaldealer buy, cook buy |
| `primitivesurvival:smokedmeat-poultry-raw` | buy | 1 | animaldealer buy, cook buy |
| `primitivesurvival:smokedmeat-bushmeat-raw` | buy | 1 | animaldealer buy, cook buy |
| `primitivesurvival:smokedmeat-redmeat-raw` | buy | 1 | animaldealer buy, cook buy |
| `hay-ud` | buy | 1 | animaldealer buy, farmer buy |
| `bonemeal` | buy | 1 | animaldealer buy, farmer buy |
| `fat` | buy | 1 | animaldealer buy, cook buy, farmer sell |
| `hide-raw` | buy | 1 | animaldealer buy, tailor buy |
| `hide-raw-medium` | buy | 1 | animaldealer buy, tailor buy |
| `hide-raw-large` | buy | 1 | animaldealer buy, tailor buy |
| `leather` | buy | 9 | animaldealer buy, tailor buy |
| `leather-purple` | buy | 1 | animaldealer buy, tailor buy |
| `leather-white` | buy | 1 | animaldealer buy, tailor buy |
| `primitivesurvival:fishmount-trout` | buy | 1 | not in the pack |
| `primitivesurvival:fishmount-perch` | buy | 1 | not in the pack |
| `primitivesurvival:fishmount-salmon` | buy | 1 | not in the pack |
| `primitivesurvival:fishmount-carp` | buy | 1 | not in the pack |
| `primitivesurvival:fishmount-bass` | buy | 1 | not in the pack |
| `primitivesurvival:fishmount-pike` | buy | 1 | not in the pack |
| `primitivesurvival:fishmount-arcticchar` | buy | 1 | not in the pack |
| `primitivesurvival:fishmount-catfish` | buy | 1 | not in the pack |
| `primitivesurvival:fishmount-bluegill` | buy | 1 | not in the pack |
| `primitivesurvival:fishmount-mutant` | buy | 1 | not in the pack |
| `petai:petcookie-meat` | buy | 1 | not in the pack |
| `petai:petnest` | buy | 10 | not in the pack |
| `petai:petnest-purple` | buy | 1 | not in the pack |
| `petai:petnest-white` | buy | 1 | not in the pack |

### Craftable Locusts

| Vanilla list | Side | Item | Ours |
|---|---|---|---|
| treasurehunter | sell | `craftablelocusts:parts-corpse` | curiodealer sell, mechanic sell |
| treasurehunter | buy | `creature-locust-bronze-hacked` | curiodealer buy, mechanic buy |

### Tailor's Delight 2.2.2

| Vanilla list | Side | Item | Ours |
|---|---|---|---|
| artisan | buy | `tailorsdelight:buttons-silver` | tailor buy, curiodealer buy |
| artisan | sell | `tailorsdelight:ruler-purpleheart` | tailor sell |
| artisan | sell | `tailorsdelight:buttons-lapislazuli` | tailor sell, curiodealer sell |
| artisan | sell | `tailorsdelight:buttons-emerald` | tailor sell, curiodealer sell |
| artisan | sell | `tailorsdelight:buttons-copper` | tailor sell |
| artisan | sell | `tailorsdelight:buttons-brass` | tailor sell |
| artisan | sell | `tailorsdelight:buttons-gold` | tailor sell, curiodealer sell |
| artisan | sell | `tailorsdelight:buttons-shells` | tailor sell |
| artisan | sell | `tailorsdelight:leatherbundle-plain` | tailor sell, carpenter sell |
| artisan | sell | `tailorsdelight:checkeredcloth-yellow` | tailor sell |
| artisan | sell | `tailorsdelight:checkeredcloth-purple` | tailor sell |
| artisan | sell | `tailorsdelight:checkeredcloth-blue` | tailor sell |
| artisan | sell | `tailorsdelight:cloth-darkblue` | tailor sell |
| artisan | sell | `tailorsdelight:cloth-darkgreen` | tailor sell |
| artisan | sell | `tailorsdelight:cloth-darkred` | tailor sell |
| artisan | sell | `tailorsdelight:twine-green` | tailor sell |
| artisan | sell | `tailorsdelight:twine-blue` | tailor sell |
| artisan | sell | `tailorsdelight:twine-yellow` | tailor sell |
| artisan | sell | `tailorsdelight:twine-darkred` | tailor sell |
| artisan | sell | `tailorsdelight:twine-darkblue` | tailor sell |
| artisan | sell | `tailorsdelight:twine-darkgreen` | tailor sell |
| artisan | sell | `tailorsdelight:panel-hide-pelt` | tailor sell, animaldealer sell |
| clothing | buy | `tailorsdelight:ruler-oak` | tailor buy |
| clothing | buy | `tailorsdelight:ruler-larch` | tailor buy |
| clothing | buy | `tailorsdelight:leatherbundle-plain` | tailor buy, carpenter sell |
| clothing | buy | `tailorsdelight:buttons-copper` | tailor buy |
| clothing | buy | `tailorsdelight:buttons-zinc` | tailor buy |
| clothing | buy | `tailorsdelight:buttons-mixed` | tailor buy |
| clothing | buy | `tailorsdelight:buttons-horn` | tailor buy |
| clothing | buy | `tailorsdelight:checkeredcloth-purple` | tailor buy |
| clothing | buy | `tailorsdelight:checkeredcloth-plain` | tailor buy |
| clothing | buy | `tailorsdelight:twine-red` | tailor buy |
| clothing | buy | `tailorsdelight:twine-purple` | tailor buy |
| clothing | buy | `tailorsdelight:leatherstrips-plain` | tailor buy, carpenter sell |
| clothing | sell | `tailorsdelight:ruler-maple` | tailor sell |
| clothing | sell | `tailorsdelight:ruler-pine` | tailor sell |
| commodities | sell | `tailorsdelight:buttons-mixed` | tailor sell |
| commodities | sell | `tailorsdelight:needle-bone` | tailor sell |
| commodities | sell | `tailorsdelight:awl-obsidian` | tailor sell |
| commodities | sell | `tailorsdelight:twine-red` | tailor sell |
| commodities | sell | `tailorsdelight:twine-brown` | tailor sell |
| commodities | sell | `tailorsdelight:leatherstrips-plain` | tailor sell, carpenter sell |
| furniture | sell | `tailorsdelight:panel-hide-pelt` | tailor sell, animaldealer sell |
| furniture | sell | `tailorsdelight:panel-leather-plain` | tailor sell, carpenter sell |
| furniture | sell | `tailorsdelight:panel-leather-red` | tailor sell, carpenter sell |
| furniture | sell | `tailorsdelight:panel-leather-purple` | tailor sell, carpenter sell |
| furniture | sell | `tailorsdelight:panel-leather-orange` | tailor sell, carpenter sell |
| furniture | sell | `tailorsdelight:panel-leather-white` | tailor sell, carpenter sell |
| luxuries | buy | `tailorsdelight:buttons-lapislazuli` | tailor buy, curiodealer buy |
| luxuries | buy | `tailorsdelight:buttons-emerald` | tailor buy, curiodealer buy |
| luxuries | buy | `tailorsdelight:ruler-ebony` | tailor buy |
| luxuries | buy | `tailorsdelight:ruler-purpleheart` | tailor buy |
| luxuries | buy | `clothes-foot-king` | tailor buy, curiodealer buy |
| luxuries | buy | `clothes-waist-king` | tailor buy, curiodealer buy |
| luxuries | buy | `clothes-lowerbody-king` | tailor buy, curiodealer buy |
| luxuries | buy | `clothes-upperbodyover-king` | tailor buy, curiodealer buy |
| luxuries | buy | `clothes-upperbody-king` | tailor buy, curiodealer buy |
| luxuries | buy | `clothes-hand-king` | tailor buy, curiodealer buy |
| luxuries | buy | `clothes-neck-king` | tailor buy, curiodealer buy |
| luxuries | sell | `tailorsdelight:buttons-gold` | tailor sell, curiodealer sell |
| luxuries | sell | `tailorsdelight:buttons-silver` | tailor sell, curiodealer sell |
| luxuries | sell | `tailorsdelight:buttons-emerald` | tailor sell, curiodealer sell |
| luxuries | sell | `tailorsdelight:twine-purple` | tailor sell |
| luxuries | sell | `tailorsdelight:twine-white` | tailor sell |
| luxuries | sell | `tailorsdelight:leather-darkred` | tailor sell, carpenter sell |
| luxuries | sell | `tailorsdelight:leather-darkblue` | tailor sell, carpenter sell |
| survivalgoods | buy | `tailorsdelight:twine-brown` | tailor buy |
| survivalgoods | buy | `tailorsdelight:leatherbundle-pelt` | tailor buy, carpenter sell |
| survivalgoods | buy | `clothes-upperbodyover-hunter-coat` | tailor buy |
| survivalgoods | buy | `clothes-waist-sturdy-belt` | tailor buy |
| survivalgoods | buy | `tailorsdelight:leatherbundle-plain` | tailor buy, carpenter sell |
| survivalgoods | buy | `tailorsdelight:leatherstrips-plain` | tailor buy, carpenter sell |
| survivalgoods | buy | `stick` | carpenter buy |
| treasurehunter | sell | `tailorsdelight:ruler-gold` | tailor sell, curiodealer sell |
| treasurehunter | sell | `tailorsdelight:buttons-emerald` | tailor sell, curiodealer sell |
| treasurehunter | sell | `tailorsdelight:buttons-lapislazuli` | tailor sell, curiodealer sell |

### Wool 1.9.6

| Vanilla list | Side | Item | Ours |
|---|---|---|---|
| artisan | buy | `wool:clothes-lowerbody-leggings-noble` | tailor buy |
| artisan | buy | `wool:clothes-lowerbody-leggings-game` | tailor buy |
| artisan | sell | `wool:twine-wool-plain` | tailor sell |
| artisan | sell | `wool:twine-wool-brown` | tailor sell |
| artisan | sell | `wool:twine-wool-black` | tailor sell |
| artisan | sell | `wool:twine-wool-gray` | tailor sell |
| artisan | sell | `wool:twine-wool-blue` | tailor sell |
| artisan | sell | `wool:fleece-muskox-brown` | tailor sell, animaldealer buy |
| artisan | sell | `wool:fleece-angora-white` | tailor sell, animaldealer buy |
| artisan | sell | `wool:fleece-generic-yellow` | tailor sell, animaldealer buy |
| artisan | sell | `wool:fleece-generic-lightbrown` | tailor sell, animaldealer buy |
| artisan | sell | `wool:fleece-generic-redbrown` | tailor sell, animaldealer buy |
| artisan | sell | `wool:clothes-upperbodyover-sweater-wool-plain-gray` | tailor sell |
| artisan | sell | `wool:clothes-upperbodyover-sweater-wool-plain-white` | tailor sell |
| artisan | sell | `wool:clothes-upperbodyover-sweater-wool-plain-pinkgray` | tailor sell |
| artisan | sell | `wool:clothes-upperbodyover-sweater-wool-plain-brown` | tailor sell |
| artisan | sell | `wool:clothes-upperbodyover-sweater-wool-fishbone-brown` | tailor sell |
| artisan | sell | `wool:clothes-upperbodyover-sweater-mohair-plain-white` | tailor sell |
| artisan | sell | `wool:clothes-upperbodyover-sweater-qivuit-plain-brown` | tailor sell |
| clothing | buy | `wool:sack-wool-plain` | tailor buy, generalstore buy |
| clothing | buy | `wool:twine-wool-brown` | tailor buy |
| clothing | buy | `wool:twine-wool-black` | tailor buy |
| clothing | buy | `wool:cloth-wool-plain` | tailor buy |
| clothing | buy | `wool:cloth-wool-brown` | tailor buy |
| clothing | buy | `wool:clothes-upperbodyover-sweater-wool-plain-gray` | tailor buy |
| clothing | buy | `wool:clothes-upperbodyover-sweater-wool-plain-white` | tailor buy |
| clothing | buy | `wool:clothes-upperbodyover-sweater-wool-plain-pinkgray` | tailor buy |
| clothing | buy | `wool:clothes-upperbodyover-sweater-wool-plain-brown` | tailor buy |
| clothing | buy | `wool:clothes-upperbodyover-sweater-wool-fishbone-brown` | tailor buy |
| clothing | buy | `wool:clothes-upperbodyover-sweater-mohair-plain-white` | tailor buy |
| clothing | buy | `wool:clothes-upperbodyover-sweater-qivuit-plain-brown` | tailor buy |
| clothing | sell | `wool:clothes-upperbodyover-sweater-wool-plain-gray` | tailor sell |
| clothing | sell | `wool:clothes-upperbodyover-sweater-wool-plain-white` | tailor sell |
| clothing | sell | `wool:clothes-upperbodyover-sweater-wool-plain-pinkgray` | tailor sell |
| clothing | sell | `wool:cloth-wool-plain` | tailor sell |
| clothing | sell | `wool:cloth-wool-gray` | tailor sell |
| commodities | sell | `wool:fleece-generic-plain` | tailor sell, animaldealer buy |
| commodities | sell | `wool:fleece-generic-brown` | tailor sell, animaldealer buy |
| commodities | sell | `wool:fleece-generic-gray` | tailor sell, animaldealer buy |
| commodities | sell | `wool:fleece-generic-white` | tailor sell, animaldealer buy |
| commodities | sell | `wool:twine-wool-plain` | tailor sell |
| commodities | sell | `wool:fibers-generic-plain` | tailor sell |
| commodities | sell | `wool:wool-plain` | tailor sell |
| survivalgoods | buy | `wool:twine-wool-plain` | tailor buy |
| survivalgoods | buy | `wool:sack-wool-brown` | tailor buy, generalstore buy |
| survivalgoods | buy | `wool:sack-wool-gray` | tailor buy, generalstore buy |
| treasurehunter | buy | `wool:sack-wool-brown` | tailor buy, generalstore buy |
| treasurehunter | buy | `wool:sack-wool-white` | tailor buy, generalstore buy |
| treasurehunter | buy | `wool:sack-wool-gray` | tailor buy, generalstore buy |

### Signals 0.3.3

| Vanilla list | Side | Item | Ours |
|---|---|---|---|
| commodities | sell | `signals:el_wire` | mechanic sell, curiodealer sell |
| treasurehunter | sell | `signals:el_wire` | mechanic sell, curiodealer sell |
| commodities | sell | `signals:blocksource` | mechanic sell, curiodealer sell |
| treasurehunter | sell | `signals:blocksource` | mechanic sell, curiodealer sell |
| treasurehunter | sell | `signals:blockscreen-east` | mechanic sell, curiodealer sell |

### Butchering 1.14.3

| Vanilla list | Side | Item | Ours |
|---|---|---|---|
| luxuries | buy | `butchering:ivory-raw` | curiodealer buy, animaldealer buy |
| luxuries | buy | `butchering:fur-bolt` | not in the pack |
| survivalgoods | buy | `butchering:fur-bolt` | not in the pack |
| treasurehunter | buy | `butchering:ivory-raw` | curiodealer buy, animaldealer buy |

### Adventurer's Walking Stick 3.0.10

| Vanilla list | Side | Item | Ours |
|---|---|---|---|
| luxuries | sell | `walkingstick:walkingstick-forlorn` | curiodealer sell |
| treasurehunter | sell | `walkingstick:schematic-flintlock` | curiodealer sell |

### Abyssal Depths 1.0.20

| Vanilla list | Side | Item | Ours |
|---|---|---|---|
| treasurehunter | sell | `abyssaldepths:ad-schematic-divinggear` | curiodealer sell |
| treasurehunter | buy | `abyssaldepths:ad-schematic-divinggear` | curiodealer buy |

### Translocator Map

| Vanilla list | Side | Item | Ours |
|---|---|---|---|
| treasurehunter | sell | `locatormap-translocator` | curiodealer sell |

## What wave 1 built

### Trader types (#448)

Eleven types (`Trading/Core/TraderTypes.cs`): smith, mechanic, prospector, farmer, cook, tailor,
carpenter, mason, animal dealer, general store, curio dealer. They replace vanilla's nine, Domestic
Animal Trader's and Culinary Artillery's in grid worlds.

- **Entities**: `seraphhorizons:trader-{gender}-{type}-{climate}` (66 codes), made from vanilla's two
  trader files by `Trading/tools/make_entities.py` (re-run after a game update; output committed):
  same shape, skins, outfits (the curio dealer wears the luxuries trader's finer set), names, AI,
  revive, vanilla's trader dialogue; class `SeraphHorizons.Trader` (`EntitySeraphTrader`).
  BetterRuins' map-quest dialogues go to the curio dealer (its luxuries trader's) and the farmer (its
  agriculture trader's) by `patches/trading-betterruins-dialogue.json`, as BetterRuins does it.
- **Hook**: no Harmony. `EntitySeraphTrader` subclasses `EntityTrader` and gives it `TradeProps` with
  the wallet and *empty* lists, so all of vanilla's stocking (spawn, import, weekly) runs, tops up the
  wallet, and adds nothing; right after each, `EntitySeraphTrader.Restock(refreshChance)` fills both
  sides from our list: after `base.OnEntitySpawn` (1.1), after `base.OnEntityLoaded` when imported
  (1.1, flagged by `DidImportOrExport`), and after `base.OnGameTick` when `lastRefreshTotalDays`
  moved (0.5). It records each slot's entry key in `WatchedAttributes`
  (`seraphhorizons:sellingkeys`/`buyingkeys`) to know which rotating goods are still there.
- **Region**: set at first spawn from the world (`RegionProbe`): climate band from the worldgen mean
  temperature (cold < 0 °C, hot ≥ 16 °C), rock group from the first `rock-*` block under the surface,
  by the game's own `RockGroup` in `worldproperties/block/rock` (igneous_*, sedimentary, metamorphic;
  mods' rocks included), falling back to a table. Saved as `seraphhorizons:region`
  (`temperate/igneous`).

### Trade list format

`assets/seraphhorizons/config/tradelists/trader-{type}.json`, read by `Trading/Game/TradeLists.cs`
into `Trading/Core/TradeList.cs`:

```json5
{
  "type": "smith",
  "campWeight": 1.0,                      // how often the grid picks this type (prospector: ignored)
  "wallet": [{ "avg": 110, "var": 20 }, …], // gears by standing tier; tier 0 for now (#452)
  "selling": {
    "core": [ entry, … ],                 // always stocked, list order, fresh at every restock
    "rotating": { "maxItems": 6, "list": [ entry, … ] },
    "regional": {                         // keys: cold, temperate, hot, sedimentary, igneous, metamorphic
      "cold":    { "core": [ … ], "rotating": [ … ] },
      "igneous": { "core": [ … ], "rotating": [ … ] }
    }
  },
  "buying": { …the same… }
}
```

An entry is vanilla's (`type`, `code`, `attributes`, `stacksize`, `stock`, `price`) plus
`"playerSupplied": true` for metal and metal goods, glass and fired goods, leather and fine cloth,
and machine parts. Unknown fields are ignored, so later waves add theirs (value overrides, standing
gates). A trader's list for its region (`TradeListResolver`): the core, then its climate key's core,
then its rock key's; the rotating pool likewise; an entry once (first wins, core over rotating); at
most 16 slots a side. Each restock (`RestockPlanner`): the core first, fresh; a player-supplied
selling entry only if the supply gate gives it stock; rotating goods still in stock stay with chance
1 − `refreshChance`, the rest are drawn from the shuffled pool. Buying sides have no gate: traders buy
player-supplied goods, which is how supply enters.

At load, every entry is checked against the registry once; one the game cannot resolve is dropped
with a single warning naming it (none in the pack: `TradingCoreScenarios`). A `TradeItem` is made
afresh from each entry's JSON for every restock.

**Curation.** Every entry of vanilla's nine trader lists and of the table above is in at least one of
our lists, keeping vanilla's price, stack and stock (a good moved from selling to buying is priced
×0.6, the other way ×1.6; every buying price was later divided by five for the buy spread, see
"Everything has a price"), and the pack's own goods are added where the vanilla lists had none:
mechanical power, pipes and steam (mechanic), ore samples and mining supplies by rock group
(prospector), seeds and saplings by climate (farmer), planks and logs by climate (carpenter), stone by
rock group (mason), young animals by climate and tack (animal dealer), everyday supplies (general
store). The tables were written with a one-off script; the JSON is the source.

### Camps on a grid (#447)

- **Grid** (`Trading/Core/TraderGrid.cs`): 2048-block cells (whole chunks). A cell's camp goes at the
  first of 8 seeded spots (≥ 192 from the cell's edge) to take one as their chunks generate, or failing
  all of them at one of the cell's second chances (below). Every 8192-block cell is a future
  settlement: no spot within 512 blocks of its centre (a corner shared by four camp cells).
- **Types per cell**: the prospector on a lattice of 3×3-cell blocks (one per block at a seeded
  offset 0 or 1 on each axis): every cell has one within two cells, and no two touch. Other cells take
  one of the ten other types by `campWeight`, never a neighbour's: greedy colouring in a seeded
  priority order, which needs only a cell's neighbourhood, so the answer doesn't depend on which cells
  were asked first.
- **Hashing**: `StableHash` (SplitMix64 over seed, an FNV-1a salt, x, z, n), its own copy; the ore
  feature (#435) has another, and the two may be unified.
- **Placement** (`Trading/Game/TraderCamps.cs`): at `InitWorldGenerator` (this system's ExecuteOrder
  0.6, after GenStructures) the `trader`-group structures are taken out of `GenStructures.scfg`
  (reflection; `config/trading/camps.json` lists the groups, the group dropped outright, and vanilla's
  multi-trader outposts kept back for settlements), their `MinGroupDistance` set to 0. A chunk column
  holding a spot of a cell still without a camp tries the camp structures in a seeded order weighted
  by their `chance`, with the climate and forest values GenStructures computes: at the spot, then at
  the chunk's other points in a seeded order, at most 48 positions whose ground suits some camp. It
  records a placed camp as GenStructures does (generated structure of group `trader`, land claim).
  Since #599 the grid does the game's surface placement itself instead of calling `TryGenerate`; see
  "Camp placement (#599)" below.
- **Order** (`CampRegistry`, savegame key `seraphhorizons:tradercamps`, #599): chunks generate in any
  order, so each cell's state is saved. Any spot whose chunk generates while its cell has no camp tries
  at once, and the first that places wins; a spot is tried once (claimed before the try, as its chunk
  generates once). Until #599 spots were tried strictly in order and a spot whose chunk generated
  before its turn was passed for good: in a real world (issue #599) every good spot of the five cells
  explored was burnt that way while each cell waited on a spot that couldn't take a camp. Strict order
  bought no determinism (the outcome already followed the order chunks generate in), only lost spots.
- **Second chances** (#599): when every spot of a cell has missed, the cell is **open**: chunks of the
  cell generated from then on may still try, but only those `TraderGrid.SecondChanceChunk` picks (wholly
  inside the cell's 192-block margin like the spots, their middle out of a settlement reserve, one in
  four by the seed: about 670 of a cell's 4096 chunks), each at its middle first and then its other
  points as a spot, and at most 24 of them (`CampRecord.Retries`); after the 24th miss the cell has no
  camp for good (**failed**). So a cell whose spots all landed on water or crags can still get a camp
  where players go, at a bounded cost: at most 8 + 24 tries per cell, ever, each at most 48 positions
  past the quick ground test.
- **Existing worlds** (#599): records saved before have `Attempt` and `Passed` and no `Tried`, and are
  migrated on load. A pending cell's spots before the one it waited for and its passed spots count as
  tried (their chunks are generated; the passed ones are lost, as their chunks can't generate again);
  the spot it waited for and the later ones not passed try as their chunks generate, in any order. A
  failed cell (all of its spots generated) becomes open, so its chunks not generated yet get second
  chances. Placed camps stay as they are; none is moved or added to chunks already generated.
- **Traders in camps**: an `onattemptspawnerspawn` listener rewrites any other mod's trader code (by
  class `EntityTrader` or a trader code) into ours: the cell's type, the spawner's gender, the outfit
  set by climate as vanilla's. Spawners keep respawning the camp's trader as before. Not a spawner
  inside a story structure (its schematic's area in `GenStoryStructures.Structures`): vanilla's
  treasure hunter is a trader by code and class, and rewritten it lost its story dialogue (and, made a
  prospector, sold a map to the treasure hunter that led to its own house); a mod's story NPCs keep
  theirs the same way. Not the landform radius around the structure (200 blocks for the treasure
  hunter), which reaches over camps the grid may place 100 blocks off. Worlds that already rewrote
  theirs keep them.
- **New worlds only**: whether a world has the grid is decided at its first start with the mod
  (new world and switch `TraderGrid` on) and saved (`seraphhorizons:tradergrid`); an existing world
  keeps vanilla camps and traders. Switching it off later stops the grid (the world's new chunks get
  the game's camps again).
- **Commands** (`Trading/Game/Commands/TradeCommands.cs`, privilege `controlserver`):
  `/sh trade camps [radius]` (cells around the caller or the spawn: id `cellX,cellZ`, type, placed camp,
  the next spot not tried, or an open or failed cell) and `/sh trade tp <id>` (to the camp; a cell not
  decided yet has its next spot generated first, which tries it; an open cell has nowhere to go). `/sh` is made with `GetOrCreate`; `TradeCommands.Trade` is the `trade` node for
  #459 to add to. The admin tools (#459) are in `Trading/Admin/` and `docs/admin-tools.md`.

### Camp placement (#599)

In a world on Conquest Landform Overhaul terrain five explored cells got no camp (#599). An offline
replay of every spot against the save (the real `TraderGrid`, the save's seed, heightmaps, climate and
forest maps, and the 12 surface camp structures' schematic sizes) found three causes, all fixed here:
the strict spot order (above), a pre-filter that didn't match the game's check, and the game's
exact-level rule. Climate was never the blocker.

- **The game's check** (`WorldGenStructure.TryGenerateAtSurface`, VSEssentials 1.22.7): one random
  schematic and one random rotation per call (`rand.NextInt(schematicDatas.Length)`, `rand.NextInt(4)`;
  `EntranceRotation` schematics excepted), then the terrain height (`GetTerrainMapheightAt`, which
  reads `WorldGenTerrainHeightMap`) at five points: the centre (start + ceil(size / 2); start is the
  schematic's min corner) and the four corners at start, +SizeX, +SizeZ and both, one block past the
  footprint and so often in the next chunk. It rejects unless all five are exactly equal, then rejects
  a centre deeper than the structure's `MaxBelowSealevel` under the sea, liquid at 13 points around the
  ground, the schematic's above- and underground check positions, a minimum group distance and an
  overlap (`WouldOverlapAt`: the map regions' generated structures, and `GenStructures`'
  `OnPreventSchematicPlaceAt`). It seats the camp at centre + 1 + `OffsetY` and places it with
  `PlaceRespectingBlockLayers(..., displaceWater: true)`. Vanilla's shallow-water camp
  (`TryGenerateInShallowWater`) wants the samples exactly one apart and water 1–2 deep at the corners.
  Before that, `TryGenerate` gates by climate: rain, temperature (at the position's height) and forest
  within the structure's range, and no cold camp more than 15 above the sea.
- **Neighbours' heights are there**: the engine runs a chunk column's TerrainFeatures pass only when
  all eight neighbours have at least finished Terrain (`ServerSystemSupplyChunks.ensurePrettyNeighbourhood`
  asks `ChunkServerThread.EnsureMinimumWorldgenPassAt` for each neighbour at the column's own pass, and
  requeues the column until they are), so their `WorldGenTerrainHeightMap`s are filled. Only ±1 chunk:
  a footprint whose samples would reach two chunks away is not tried (`CampGround.InNeighbourhood`).
- **The grid's own placement** (`TraderCamps.TryCandidate`, logic in `Trading/Core/CampGround.cs`):
  the same checks in the same order, for every schematic in all four rotations (a seeded order,
  `TraderGrid.CandidateOrder`) before moving to the next structure or position, instead of one random
  draw. The structure's internal `schematicDatas`, `resolvedRockTypeRemaps` and
  `replacewithblocklayersBlockids` are read by reflection; the rest is public. A rotation's footprint is
  the schematic's with the sides swapped on a quarter turn (the rotated copies are sized only once
  unpacked, and the real size is checked after unpacking).
- **Pre-filter**: a position counts towards the 48 only if some camp structure's footprint (any
  schematic, any rotation) passes the five samples and the sea-depth limit, read from the chunk's and
  its neighbours' heightmaps. The old filter probed +4/+8/+12 inside the spot's own chunk, so most of
  the budget went on positions the game then rejected and many it would take were never tried.
- **Slope tolerance and levelling**: a surface camp takes ground whose five samples differ by up to
  `slopeTolerance` (`config/trading/camps.json`, 2; 0 is the game's rule), seated on their median
  (`RuinSurfaceHeight.Median`, as `RuinsOnMedianGround` seats ruins; with all five equal, the game's
  own height). The liquid checks and the above- and underground checks are the game's, against the
  levelled ground (inside the footprint the ground is at the base; a block the levelling fills counts
  as ground, one it cuts does not). If the checks pass, the terrain under the footprint is levelled
  to the base first: a lower column is filled with its own soil (the block under its top; the top block
  goes back on top), a higher one cut down with its top block put back on the cut, and the chunk's
  `WorldGenTerrainHeightMap` and `RainHeightMap` follow, so the schematic's soil layers and later passes
  see the new ground. A footprint with a column more than 6 off the base (a ravine or crag the samples
  missed) is not taken. The shallow-water camp keeps the game's rule exactly.
- **Why not a switch**: the tolerance and levelling are how the grid places a camp, part of
  `TraderGrid`, and only exist with it on; `slopeTolerance: 0` gives back the game's ground rule. A
  separate switch would split the grid into combinations to test for no player-facing gain.
- **Replay** of the five cells of #599's save with these rules (liquids, check positions and story
  structures not modelled): of 23 generated land and sea spots, 10 have a position that takes a camp at
  tolerance 0 and 16 at tolerance 2; 4 of the 5 cells have such a generated spot (the fifth, 249,250,
  has only its two generated spots in the sea, and its other six are still to generate), where none of
  them got a camp before. In the Atlas test world (seed 436447448) the spawn's cell and its first
  neighbour each took a camp at their first spot, both on ground 2 uneven, levelled.

## Standing and companies (#452, #463)

`Trading/Standing/`: `Core/Standing.cs` (records, tiers, spillover, the ledger), `Core/Company.cs`
(company choice and merge rules), `Game/StandingSystem.cs` (server `ModSystem`, ExecuteOrder 0.61,
switch `TraderStanding`), `Game/GroupHooks.cs`, `Game/StandingCommands.cs`, and
`config/standing-tiers.json`.

- **Records**: per (player uid, trader id) and per (group uid, trader id): points (never below 0),
  the day they last changed, the last `eventsKept` changes. Saved as one JSON blob in the savegame
  (`seraphhorizons:standing`; a blob that fails to load is kept under `….broken` and the world starts
  afresh).
- **Trader id**: `camp:x,z` when the trader stands within 96 blocks of its cell's placed camp (the
  registry, `CampRegistry`), else `entity:<EntityId>`; decided once and kept in the trader's
  `WatchedAttributes` (`seraphhorizons:traderid`). Camp ids survive the spawner replacing a trader;
  entity ids do not, which only matters for traders outside camps (vanilla worlds, `/entity spawn`).
- **Points**: a deal earns `perGear` × (gears paid + gears received) at the dialog's prices, so it
  scales with the value of the goods. Orders `order`; a delivery `delivery` at the receiver, and at
  the sender too when on time (`bothEnds`). A failed delivery takes `deliveryFailed` at the sender, an
  abandoned order `orderAbandoned`.
- **Tiers**: stranger 0, known 60, regular 250, trusted 800, partner 2000 points of effective
  standing. Unlocks (`TierUnlocks`): `mapTier`, `mapsToTraders`, `buyPriceFactor`, `sellPriceFactor`,
  `walletTier`, `orderScale`, `deliveryScale`, `rareStock`. Consumers: the wallet and the shelf
  (`walletTier`, `rareStock`, and every entry's `standingTier`), prices (`buyPriceFactor`,
  `sellPriceFactor`), maps (`mapTier`, `mapsToTraders`), orders (`orderScale`) and deliveries
  (`deliveryScale`); every unlock has a consumer.
- **Effective standing** = max(personal, company) + `spilloverShare` (0.1) × the best max(personal,
  company) at another trader of the same type within `TraderStandingSpilloverKm` (6). Same-type
  traders come from the grid's placed camps, so only camp traders spill over.
- **Deal hook** (no Harmony): every deal credits standing from `EntitySeraphTrader.AfterDeal`: the
  trade window's (`BuyUnit`/`SellUnit`, see "The trade window"), and the game's packet 1000 (vanilla's
  dialog's button, which the pack's traders still honour), whose `InventoryTrader.TryBuySell` is
  internal and reports success only to the base class, so `OnReceivedClientPacket` reads
  `GetTotalCost`/`GetTotalGain` first and compares the carts after: a deal that went through empties
  the buying cart and takes the sold goods out of the selling cart, a failed one changes neither.
- **Display**: the trade window's header (tier, a bar to the next, the raw numbers) and Standing tab,
  and the trader's answer to the dialogue's "How do you see me these days?" (see "The trade
  window"). A deal that lifts the player a tier still says so in chat. (Until the window, opening the
  trade posted the standing to chat once a visit; that line is gone.)
- **Wallet**: before vanilla's weekly top-up runs (`OnGameTick`, when `lastRefreshTotalDays` is more
  than 7 days back), `EntitySeraphTrader` sets `TradeProps.Money` to the list's wallet for
  `IStandingSource.WalletTierFor`: the best `walletTier` among players whose own record with the
  trader changed in the last `recentDays` (14). Chosen over the interacting player's tier because the
  top-up happens with nobody there; vanilla's top-up only moves 7–28 % towards the target a week, so
  a trader's wallet grows over a few weeks of trading.
- **Companies**: a player's company is the group they chose (`/sh company`), if they are still in
  it and it exists, else their first group that exists (the server's membership dictionary, in join
  order), else none. Every read resolves it live from `sapi.PlayerData` / `sapi.Groups` and syncs
  (`CompanyBook.Sync`): a company the player is new to takes max(company, personal) per trader and
  records them as merged; a company they are merged with but no longer in forgets them and keeps its
  points. Merging once per membership matters: a penalty to the company must not be undone by the
  next read lifting it back to a member's personal record. Harmony postfixes on
  `ServerPlayerData.JoinGroup(PlayerGroup, EnumPlayerGroupMemberShip)`, `LeaveGroup(PlayerGroup)`,
  `LeaveGroup(int)` and `PlayerDataManager.RemovePlayerGroup` (bound by name with
  `AccessTools`; every `/group` path goes through them) run the same sync at once, record the first
  group joined as the company, and drop a disbanded group's record. Missing methods are logged and
  skipped; the lazy sync still holds.
- **Known gaps**: a public group run by a veteran lends full standing to anyone in it while they stay
  (accepted in #463). A player who leaves and rejoins between two reads is not re-merged (harmless:
  max). Groups are server-wide and records per world, so a group uid from another world's records
  means nothing until it exists here.

### For the integrator and later waves

- `TradingSystem.Standing` (`IStandingSource`, `Trading/Standing/Game/IStandingSource.cs`):
  `TraderIdOf(trader)`, `TierFor(player, trader)`, `UnlocksFor(player, trader)`,
  `PriceFactorFor(player, trader, PriceSide.PlayerBuys|PlayerSells)`, `WalletTierFor(trader)`,
  and the wave 3 hooks `OnOrderDone(playerUid, traderId)`,
  `OnDeliveryDone(playerUid, fromTraderId, toTraderId, bothEnds)`,
  `OnDeliveryFailed(playerUid, fromTraderId)`, `OnOrderAbandoned(playerUid, traderId)`. With the
  switch off it is `NoStanding` (tier 0, factor 1, hooks do nothing).
- Pricing: multiply a slot's price by `PriceFactorFor` for the player at the trader. Prices live in
  the shared `InventoryTrader` slots and every player sees the same dialog, so a per-player price has
  to be applied where the deal is priced for that player (`GetTotalCost`/`GetTotalGain` read
  `TradeItem.Price`), not when the shelf is filled.
- Stock by tier (`rareStock`, maps): `TradeListResolver.Resolve(def, region, tier)` takes a tier;
  shelves are shared, so tier-gated stock needs either the best recent tier (as the wallet) or a
  per-player check at the deal.

## Extension points for later waves

- **Supply** (#450, #451): set `TradingSystem.SupplyGate` (`ISupplyGate.Stock(TraderContext, TradeEntry)`);
  call `EntitySeraphTrader.Restock` when supply changes. Selling sides only.
- **Pricing**: entries carry vanilla prices; a value system replaces `ResolvedTradeItem.Price` in
  `EntitySeraphTrader.Fill` (one place) or adds a field to entries.
- **Standing** (#452, #463): built; `TradingSystem.Standing` (see "Standing and companies").
- **Everything has a price**: hook `InventoryTrader.GetBuyingConditionsSlot` / `IsTraderInterestedIn`
  (Harmony on our traders only) to accept unlisted goods at a lower price.

## Prices and supply (#450, #451)

`Trading/Economy/`: `Core/` (`TraderRelations.cs` with `BuyerIndex`, `Pricing.cs`, `Supply.cs`;
unit-tested in `tests/Trading/Economy/`), `Game/` (`EconomySystem`, `EconomyPatches`,
`EconomyCommands`).

### Values (#449)

The value table (`config/item-values.json`, built by `tools/item-values`; the mod README's "Item base
values") is what off-list prices start from and what the list-pay tests hold the lists to.

- **Schematics** have no value: they are kept on crafting and traders are their only source, so
  `tools/item-values` never prices one, and a recipe that uses one (MachineSchematics' gates) is
  priced by its consumed parts and labour only. A trader sells them at its list's price, and the
  curio dealer buys back the diving gear schematic at its list's price; the value check exempts
  them. (#506)
- **The steel gear** (`seraphhorizons:gear-steel`) takes its cheapest route, like any item: the
  gear cutter, or the reclamation lottery (ten oiled gears less the nine steel bits the failed
  rolls give). The large steel gear (`seraphhorizons:largegear-steel`) takes its gear cutter route.
  Neither is a hand price. (#506, #523)

### Everything has a price

- **Buy spread**: a trader pays a fifth of what goods are worth (`BuySpread`, default 0.2, server
  config, synced to clients per trader as `seraphhorizons:buyspread`), a pawnshop's spread, and asks
  the full price when it sells. It applies to everything a trader buys from a player: off-list goods
  at runtime, and list entries because the lists hold the final pay (below).
- **Fit**: `config/trading/trader-relations.json`. Listed 1, a related type 0.75, otherwise 0.5. The
  relations are smith–mechanic, smith–prospector, prospector–mason, carpenter–mason,
  carpenter–mechanic, farmer–cook, farmer–animal dealer, cook–animal dealer and tailor–general store
  at 0.75; tailor–animal dealer, general store–cook and general store–carpenter at 0.6; and the curio
  dealer with everyone at 0.6. An item's fit at a trader is the best relation between its type and
  any type whose list buys the item (any region, core or rotating; `BuyerIndex`).
- **Prices** (`Pricing`): a listed entry is its list average (vanilla's rolled spread is dropped once
  the economy prices a trader) × supply × modifiers. An off-list good is the value table's value
  (scaled by remaining durability) × spread × fit × supply × modifiers. Listed goods keep the list's
  price because the lists are curated and already hold the spread: their buying prices were rescaled
  by 0.2 (`Trading/tools/rescale_buying.py`, 2026-10-06), so they sit at 0.19 × the table's value at
  the median, and `BuySpread` does not touch them. `tests/Trading/Economy/ShippedListPayTests.cs`
  holds the two together: the median in 0.15–0.25, every entry at most 0.3 × value except the
  outliers the rescale found (`tests/Trading/fixtures/list-pay-outliers.json`, mostly items the
  table prices low, each held to 1.25 × its share then), and no list paying more than 0.6 × the
  lowest list ask for the same item (the rule a runtime cap used to enforce). The game prices a
  trade in whole gears per trade-item stack, so an off-list good of less than a gear an item is sold
  by the fewest items worth a gear (`UnitSize`); under a gear per full stack it is refused
  (`TooCheap`). A listed buying entry under a gear per stack is bought by the fewest whole multiples
  of its stack worth a gear, up to the item's stack size (`Pricing.Listed`; the slot's trade stack is
  resized at every reprice), rather than rounding a fifth of a gear up to a whole one.
- **Refusals**: code prefixes under `refused` (`seraphhorizons:oremap`, `gravelmap`, `traderlead`,
  `game:locatormap`), items with a `currency` attribute, `IsWorthless` items (floorZero), items with
  no value or family value. The prefixes apply only to off-list goods; a list that names one buys it.
- **Side budget**: `WatchedAttributes["seraphhorizons:sidebudget"]`, set to ¼ of the list's tier-0
  wallet average at every restock (`EntitySeraphTrader.Restocked`). The main wallet stays vanilla's
  money slot.
- **Hook points** (Harmony, `EconomyPatches`, id `seraphhorizons.economy`, patched once per process,
  acting only on an `InventoryTrader` whose trader is ours with `seraphhorizons:everythingpriced` set):
  - `InventoryTrader.GetBuyingConditionsSlot` postfix. Vanilla decides everything about a sale
    through it: `IsTraderInterestedIn` (so `ItemSlotBuying.CanHold` and shift-click),
    `HasTraderEnoughDemand`, `GetTotalGain`, and the deal's own loop. Where it finds no buying slot,
    the postfix returns an `OffListSlot` (an `ItemSlotTrade` outside the inventory, stock 9999) with
    the offer, and the good then sells exactly like a listed one.
  - `InventoryTrader.TryBuySell` (internal) prefix and postfix. The prefix totals the off-list gain,
    refuses the deal (`TraderNotEnoughAssets`, with an in-game error) when it is more than the side
    budget, and on the server moves that much from the side budget into the money slot, so vanilla's
    own payment works unchanged. The postfix moves it back if the deal failed; after a deal it
    records supply and re-prices the region's loaded traders before vanilla broadcasts the inventory
    (packet 1234).
  - `ItemSlot.GetStackDescription` postfix, selling-cart slots (`ItemSlotBuying`) only: the breakdown
    and which budget pays. `ItemSlotBuying.CanHold` postfix (client): why a good is refused.
  - (Until the trade window, `InventoryTrader.GetTraderAssets` and two of `GuiDialogTrader`'s private
    methods were patched too, to show the side budget in vanilla's dialog and count it in its local
    check; the pack's traders no longer use that dialog, and the window shows the side budget
    itself, so they are gone.)
- **Both sides compute**: the client needs the price before the server sees the deal (the cart's
  `CanHold`, the gain text). `ItemValuesSystem` loads on both sides; the client loads the lists at
  `LevelFinalize` for the `BuyerIndex`. Per trader the server syncs `everythingpriced`, `sidebudget`
  and `supplyfactors` (every item of the trader's supply region whose factor is under 0.999). The
  server prices from the same synced factors, so both sides agree. The factors are refreshed at a
  restock, at the daily tick, after any deal in the region, and on `/sh trade supply` changes.
- **`IPriceModifier`** (`EconomySystem.Modifiers`): `double Factor(in PriceContext)` with item code,
  trader type, supply region, direction, trader id and player uid (null when no player is known: shelf
  prices). Factors multiply after supply. The standing wave adds one on both sides from data both sides
  have (e.g. the trader's watched attributes). A modifier that depends on the player should only act
  where `PlayerUid` is set: listed shelf prices are computed without one.

### Regional supply

- **Region key**: 8192-block squares (`SupplyRegion`, `rx,rz`), the grid's settlement cells, 4 × 4
  camp cells. Coarse enough that the camps a player visits share a market, fine enough that hauling to
  the next region pays.
- **Level**: per (region, full item code), in units of 10 gears' worth (`ReferenceGears`). Selling n
  items adds n × value / 10 (table value, else the price traded at); buying drains the same, clamped at
  0. Daily: × 0.5^(1/half-life), then 10 % moves to the eight neighbours, weighted 1 orthogonal and
  1/√2 diagonal, normalised. Levels under 0.01 are dropped. History: the last 16 changes per entry.
- **Price curve**: `f(L) = 0.3 + 0.7 / (1 + L / 5)`. 1 at 0, 0.65 at 5 (50 gears' worth sold), never
  under 0.3. With the defaults, ten iron ingots a day for a week leave the region at about 0.5×, the
  next region a few percent down, and a month later back above 0.9×
  (`OnePlayersOutputReachesTheNextRegionWithoutAWeekLongCrash`).
- **Shelving** (`RegionalSupplyGate`, `TradingSystem.SupplyGate`): from level 1, stock is
  floor(0.5 × level × 10 / (value × stack size)), at most 2 × the entry's stock.
- **Clock**: a 2 s server tick listener runs one `Tick` per elapsed calendar day (at most 30 at once).
  It is saved as `seraphhorizons:supply` (JSON) and `seraphhorizons:supplyday`.
  `/sh trade simulate <days>` ticks the book, raises `EconomySystem.SimulatedDay` per day (for orders
  and deliveries), and moves each loaded trader's `lastRefreshTotalDays` back by the days, so vanilla's
  weekly loop restocks it on its next check.

### Decisions

- **2026-10-06: a trader pays a fifth of value; fit flattened to 1 / 0.75 / 0.5** (was 1 / 0.5 / 0.2,
  weak links 0.3, now 0.6; and a buying price capped at 0.6 × what the trader asks, now gone). A steel
  gear rusts in brine 1:1 into a rusty gear, the currency, so what a trader pays for one had to sit
  well under its value, or reclaiming gears would print money. A pawnshop's spread does that for
  everything at once, and with the fit flattened, hauling goods to the trader who wants them still
  pays (a good fit doubles the pay, not quintuples it) without a poor fit being a refusal in all
  but name. The lists hold the final pay (rescaled once by 0.2) rather than the runtime dividing
  them, so a list's numbers are what a player is offered; the runtime spreads off-list goods only.
  The mechanic took in the pack's reclaimed steel gear and large gear (`seraphhorizons:gear-steel`
  at 13 and `largegear-steel` at 24, their derived values rounded, player-supplied; bought at 2.6
  and 4.8, a fifth of that), filling the hole left when ppex's gears were dropped (#507). (These
  were 15 / 3 and 19 / 3.8 while the steel gear was a hand price of 15; the item-values merge
  derived it, 10.895, and rescaled both entries to 11 / 2.2 and 22 / 4.4; the gear cutter's
  dearer frame and parts then moved the derived values to 12.6 and 23.7.)

## Extension points for later waves

- **Supply** (#451, done): `EconomySystem.Supply` (`SupplyBook`); the gate is set at GameReady.
- **Pricing** (#450, done): `EconomySystem.Modifiers` (`IPriceModifier`) for standing;
  `EntitySeraphTrader.Restocked` for anything that must follow a restock;
  `EconomySystem.SimulatedDay` for clocks that `/sh trade simulate` should advance.
- **Standing** (#452, #463): `TradeListDef.WalletFor(tier)`, `TradeListResolver.Resolve(def, region, tier)`;
  per-trader data can live in the entity's `WatchedAttributes` like the region.
- **Orders, deliveries, maps**: dialogue components on the trader (`Dialog_DialogTriggers` is
  protected virtual: override in `EntitySeraphTrader`).
- **Camps**: `TraderCamps.Registry` (placed camps with type, position, region, structure) for maps to
  other traders and deliveries; `TraderGrid.TypeOf`/`Spots` for where camps will be.

## Open problems

- The handbook's and the recipe browser's "sold by" read vanilla-format lists from entity attributes
  (`tradePropsFile`/`tradeProps`); our types have none, so neither shows them. In a grid world both
  leave out the traders the grid replaces (seraphhorizons README, "Traders"), so they list only the
  story traders (the treasure hunter) and villagers. Showing ours needs either a flattened
  vanilla-format view per type or the exporter learning our format.
- A camp's spots are decided as chunks generate, so two worlds of the same seed explored in different
  orders can end up with a camp at a different spot of the same cell (or none). `/sh trade camps`
  shows what each world did.
- Camps are not placed in cells that are mostly ocean or high ground: `TryGenerate` refuses spots
  above sea level + 15 in climates under 20 °C, as for vanilla camps.
- Villager lists (`villager-*.json`) belong to the story villagers and are left alone.
- **Vanilla: `GenStructures` throws in worlds without lore content.** `GenStructures.initWorldGen`
  returns early when the world config's `loreContent` is false (and when the story structures config
  is missing), before it sets its private `spawnPos`; `DoGenStructures` then passes that null to
  `BlockSchematicStructure.SatisfiesMinSpawnDistance` for any structure with `minSpawnDistance` > 0
  (vanilla's `specialsurfaceruins`, 10,000), and the TerrainFeatures pass logs a
  `NullReferenceException` for that chunk column. The structures after it in that column's shuffled
  order are not tried, so such worlds get fewer ruins. Measured 2026-10-06: an Atlas world (standard,
  play style `creativebuilding`, whose config has `loreContent` false) on `origin/main`, without the
  camp grid, logged it for 2,449 distinct chunk columns (49 asked for, the spawn area and neighbours);
  `packtool smoke` and the ore survey (`surviveandbuild`, lore content on) log none. Not ours: the
  camp grid only takes the `trader` group out of `scfg` and runs `TryGenerate` itself, which never
  reads `spawnPos`. Players hit it in a world created with lore content off (the Homo Sapiens play
  style). Nothing in `pack/known-errors.json`: neither smoke nor the boot-log scenarios see it. A fix
  would be a postfix on `initWorldGen` that sets `spawnPos` as its last lines do.

## Schematics (#468, #469)

`Trading/Schematics/`, switches `TraderSchematics` and `MachineSchematics`, data in
`config/schematic-gates.json` ("sold": the schematic code patterns; "gates": machine → gated recipe
outputs, each with its mod; "sales": seller types and standing tier per schematic, which
`tests/Trading/Schematics` holds the trade lists to). Server side only: clients get item types and
recipes from the server.

**Where schematics came from** (1.22.7 and the pinned mods):

| Schematic | Sources found | What we do |
|---|---|---|
| `game:schematic-glider` | the Resonance Archive story structure's chest; the game's `schematiccopy` recipe (enabled!) | structure → parchment; recipe removed |
| `game:schematic-customtranslocator` | Tobias' dialogue (story reward) | kept: his translocator's repair needs it |
| `betterruins:br-schematic-*` (30) | 28 entries appended to vanilla's `*-gear` stack randomizers (`stackrandomizers-gear.json`); chests in 22 structure schematics (story locations, mega and large ruins); `brschematiccopy` (ships disabled, a ConfigKit setting enables it). Its panning patch adds only a locator map | randomizer entries stripped; structures → parchment; copy removed if enabled |
| `cartwrightscaravan:cartschematics-*` | crafted from parchment + charcoal (`carts`, `signs`; `canopies`, `sides` disabled) | recipes removed; no loot |
| `abyssaldepths:ad-schematic-divinggear` | `addmerge` into four vanilla stack randomizers; `adschematiccraft` and `adschematiccopy` (not registered in the pack) | randomizer entries stripped |
| `walkingstick:schematic-flintlock` | only the treasure hunter's list | — |

No vanilla loot vessel, and no panning table (vanilla, BetterRuins, Wilderlands), holds a schematic.

**Why not JSON patches.** Most entries are appended by other mods' patches (`/-`, `addmerge`), so no
patch of ours can name their index (patch files apply in asset order, not mod order); structure chests
live in multi-megabyte schematic files whose item code table is the only handle; and patches can't
read our switches. So: the loot entries go from the type assets in `AssetsLoaded` between the patch
loader (0.05) and the type loader (0.2) — after that the collectibles have copied the lists
(`ItemStackRandomizer.Stacks` in OnLoaded). Structures: a postfix on `BlockSchematic.Remap`, which
`LoadSchematicsWithRotations` and `BlockSchematicStructure.Init` (story structures unpack through it)
call, maps sold codes in `ItemCodes` to the replacement, for `BlockSchematicStructure` only. The
game's own remaps (`config/remaps.json`, `/iir`) would also reach existing saves' item mappings, so
they are not used.

**Recipes**, at `ModsAndConfigReady` (LoadGamePre): after every mod's AssetsFinalize (Immersive
Woodworking registers its frames there), before the recipes packet (after WorldReady), while the
server still has `Ingredients` and `IngredientPattern` (dropped by `FreeRAMServer` when the packet is
built). A gated recipe gets a new key in `Ingredients`, a new pattern and size, then `Resolve` again
(`GridGate.Place`: first empty slot, else a column, else a row, else two slots of the most frequent
consumable ingredient merged at double quantity if the stack fits). `GridRecipe.Enabled` is only read
at load, so removed recipes are taken out of `World.GridRecipes`. Non-grid construction (the water
wheel's stages, ppex's exlib construction, Gondola's construction sites, the biplane on its trestles)
is gated through the grid recipe of its first stage. A one-slot, consumed, make-one recipe is a
conversion (Scrolled, MadMechanics) and is neither gated, removed nor made to keep its input.

**Kept on crafting**: `noConsumeOnCrafting` (vanilla's, Cartwright's, the walking stick's) skips
consumption for *every* recipe, which would make Scrolled's rolling a copy; Scrolled removes it (the
walking stick's on the server only), so we enforce `Consume = false` on recipe slots instead. A
non-consumed ingredient returns early in `OnConsumedByCrafting`, so a `returnedStack` would not
double, but it is cleared anyway.

**Standing gate.** `TradeEntry.StandingTier` (default 0): `TradeListResolver.Resolve(def, region,
tier)` leaves out entries above the tier; the core is the ungated entries, then the gated ones lowest
tier first, cut at 16. `Problems` counts every core entry (the top tier) against the 16 slots. The
trader's restock passes the shelf tier (see "Wave 2 glue"). Slot pressure decided the
sellers: the issue's mapping put 20+ schematics on the mechanic and carpenter, so the wooden machines
went to the carpenter, the metal gear work to the smith, chest/crate/candle/toymaker/artisan and the
hand crank to the general store, book/alchemist/texture flipper to the curio dealer, and a few plain
core goods (the carpenter's planks, the mechanic's ppex gears and blades, the smith's charcoal, borax
and copper nails) moved to rotating; every list keeps at most 14 core entries at the top tier.

## Orders and deliveries (#453, #454)

`Trading/Orders/` and `Trading/Deliveries/`: `Core/Orders.cs` (`Order`, `OrderPlanner`,
`OrderBook`), `Core/Deliveries.cs` (`Delivery`, `DeliveryPlanner`, `DeliveryBook`), unit-tested in
`tests/Trading/Orders/` and `tests/Trading/Deliveries/`; `Game/OrdersSystem.cs` and
`Game/DeliveriesSystem.cs` (server `ModSystem`s, ExecuteOrder 0.66, after the economy), the commands,
`Orders/Game/TraderFinder.cs` (loaded traders by standing id, wallet and gear helpers, shared by both),
and `Deliveries/Game/ItemPackage.cs` with `itemtypes/package.json`. Switches `TraderOrders` and
`TraderDeliveries`; saved as JSON under `seraphhorizons:orders` and `seraphhorizons:deliveries` (a blob
that fails to load is kept under `….broken`).

**In the trade window.** Players take orders and hand them in, and take, mark and hand in
deliveries, in the trade window's Orders and Deliveries tabs (see "The trade window"), whose
requests call the systems' own handlers (`OrdersSystem.Accept`/`HandIn`, `DeliveriesSystem.Begin`/
`HandIn`). They were chat commands (`/sh order …`, `/sh delivery …`) until the playtest; those are
gone, and so is the chat summary of what is on that opening the trade posted. The admin commands
(`/sh trade orders …`, `/sh trade deliveries …`) stay.

### Orders

- **Generation**: on `EntitySeraphTrader.Restocked` (spawn, import, every weekly restock) a trader
  tops its open orders (offered or taken) up to 1 or 2 (a coin flip each restock). Candidates are its
  list's buying side for its region (`TradeListResolver.Resolve`, core and rotating pool, so the
  region's goods too), plain stacks with a price; the price per item is the list average over its
  stack size times the region's supply factor. Never two open orders for one item at one trader.
- **Size and premium**: at standing scale 1 an order is worth `BaseGears` (5) at that price, in whole
  lots of the list's stack size, at most four stacks; the premium factor is 1.3–1.6 (steps of 0.05),
  the premium (factor − 1) × quantity × price, at least a gear. It is taken out of the trader's wallet
  (`InventoryTrader.DeductFromTrader`) when the order is made; a wallet that can't cover it makes no
  order. Taking an order scales its quantity by the player's `orderScale` (in lots, up to four
  stacks) and holds back the larger premium, shrinking the order back towards the offer as far as the
  wallet falls short. `orderScale` 0 gives that player no orders; standing off counts as 1.
- **Since the buy spread** (2026-10-06): `BaseGears` went from 24 to 5 (24 × 0.2, rounded). The normal
  price is the list's buying price, now a fifth of value, so at 24 an order would have asked five
  times the items (up to the four-stack cap) for the same gears; at 5 it asks about as many as before,
  and its premium, a share of that price, is a fifth of what it was.
- **Delivery**: an item counts when the player sells it to the trader (the trade window)
  (`EntitySeraphTrader.Dealt`, the stacks that left the selling cart in a deal that went through) or
  hands it in from the Orders tab (what they carry of the item in hotbar and backpack, up to what
  is still wanted, paid at the order's price per item from the wallet, refused if the wallet can't
  pay). Each item pays its share of the premium at once
  (floor of premium × delivered / quantity, less what was paid); completion pays the rest and calls
  `OnOrderDone`. Hand-ins by command don't move supply; deals do, as any deal.
- **Time**: an offer lapses `days` (3–6) after it was made; a taken order's deadline is `days` after
  it was taken. Past it (`OrderBook.Tick`, a 5 s listener and every simulated day): an offer expires,
  a taken order with nothing delivered is abandoned (`OnOrderAbandoned`), one delivered in part
  expires without penalty. The premium not yet paid goes back to the trader's wallet if it is loaded,
  else it is gone (the weekly top-up refills the wallet). Closed orders are dropped after 30 days.
- **Simulate**: `EconomySystem.SimulatedDay` moves every open order's dates back a day and ticks.

### Deliveries

- **Offer**: per (sender, player, calendar day), seeded with `StableHash` from the world seed, so it
  doesn't change while the player decides. Destinations are the grid's placed camps
  (`TraderCamps.Registry`) between 300 blocks and `deliveryScale` × 3 km away, of another type than
  the sender where any is in reach. Value 20 × max(1, scale) gears ±20 %; deposit 10–30 % and fee
  20–40 % of it, at least a gear each. `deliveryScale` 0 (strangers) gets no offer; standing off
  counts as 1. One active delivery per player per sender.
- **Deadline**: `DeliveryPlanner.DeadlineDays` = max(1, km × 1) game days (`DaysPerKm`, `MinDays`),
  km being the straight distance from sender to receiver. So 2 km gives two game days, 300 m one.
  Then a grace of one game day (`GraceDays`) in which it is late. Sleeping skips game time and so
  eats into the deadline, as it would for walking.
- **Since the playtest** (2026-10-08): the deadline was the walk in real time, 5 minutes a km with
  half again as slack and at least 5 minutes, turned into game days at the world's calendar speed
  (`GameDaysPerRealMinute`, now gone): 2 km gave 7.5 game hours, a night's sleep. Game days a km
  need no calendar. A delivery keeps the deadline it was made with (`deadline` and `grace` are
  saved as total days), so ones taken before the change run out as they would have. The window and
  the package say the time left in days, or in hours under a day.
- **Package**: `seraphhorizons:package`, stack size 1, the linen sack's model without its bag
  behaviours (it can't be opened), attributes `deliveryId`, `from`, `to`, `toType`, `toX`, `toZ`,
  `deadline` (total days), and `failed`. It is not in the value table, so no trader buys it.
- **Hand-in** (the receiver's Deliveries tab, the player who took it, a live package in their
  inventory): on time, the deposit back, the fee from the receiver's wallet (as far as it has it) and
  `OnDeliveryDone(bothEnds: true)`; late, the deposit and half the fee (rounded up) and
  `OnDeliveryDone(bothEnds: false)`. Past the grace (`DeliveryBook.Tick`): `OnDeliveryFailed`, the
  deposit is kept by nobody, and the package turns to junk (`failed`) in the player's inventory now
  if they are online, else at their next join.
- **Simulate**: as orders, a day's shift per simulated day.

### For the integrator and later waves

- **Hooks in `EntitySeraphTrader`** (shared with #455 maps/leads, #456 visitors, #459 admin tools):
  every deal (the window's, and packet 1000) snapshots the selling cart, calls standing and raises
  the static `Dealt(player, trader, sold)`; opening the trade window (`opentrade`) raises the static
  `TradeOpened(player, trader)`; meeting the trader (a conversation starting, or the window
  opening) raises `Met(player, trader)`. Other features should subscribe to these rather than
  override the methods again.
- `OrdersSystem.Book` / `DeliveriesSystem.Book` for inspect and export tools; `OrderCommands.AdminLine`
  and `DeliveryCommands.AdminLine` format one record.
- Not done: posting to the notice board; deliveries for traders outside camps
  (only admins can make those).

## Wave 2 glue

Standing, the economy and the schematics' tiers were built in parallel; these are the seams that
join them (`Trading/Glue/StandingPrices.cs` and small edits listed with each).

- **Prices follow standing.** Vanilla lets one player at a time trade with a trader
  (`tradingPlayerUID`, set when the dialog opens, cleared on close, walking away or death), so the
  shelf is priced for that player. `TradingGlueSystem` (both sides, ExecuteOrder 0.67) adds
  `StandingPriceModifier` to `EconomySystem.Modifiers`; on the server it writes the trading player's
  `buyPriceFactor` and `sellPriceFactor` into the trader's watched attribute
  `seraphhorizons:standingprice` (`uid`, `buy`, `sell`) when the trade window opens
  (`EntitySeraphTrader.TradeOpened`, raised from `Dialog_DialogTriggers`) and checks it every second
  (a tier reached in a deal, the player gone), re-pricing and sending the shelf through
  `EconomySystem.Refresh` when it changes. The modifier reads that attribute on both sides, for the
  context's player or else the trading player, so listed prices, off-list offers and the client's
  quotes all carry it. Nobody trading, factor 1. Listed prices are re-priced only with
  `EverythingHasAPrice` or `RegionalSupply` on (the economy owns the shelf prices); with both off,
  standing changes no price. `TradingGlueSystem.TradingPlayerPriced` is raised before each re-price
  for anything else priced per player (map precision).
- **Shelves follow the best recent customer.** `IStandingSource.ShelfTierFor(trader)`: the highest
  tier index among players whose record with the trader changed within `recentDays` (14), as the
  wallet's `WalletTierFor`. `EntitySeraphTrader.Restock` resolves the list at that tier and with that
  tier's `rareStock` (`IStandingSource.UnlocksOfTier`). A stranger therefore sees what a trusted
  customer unlocked until the first restock after that customer has been away 14 days; prices and
  map precision are still the stranger's own, and the settlement lead is refused to them at the
  deal (camp leads are per buyer, off the shelf). `EconomySystem.Reprice` resolves the list at the top tier with rare stock, so every entry
  a shelf may hold is priced.
- **Rare stock.** `"rare": true` on a list entry (`TradeEntry.Rare`): shelved only when the shelf
  tier's `rareStock` is on (trusted and partner). Marked on three selling entries per list (the
  smith's anthracite only), the dearest goods of the rotating pools that are neither player-supplied
  nor schematics: tame elk and the living dead, purpleheart, ebony and redwood doors, iron and bronze
  crocks, the forlorn armour, bells and translocator maps, redwood and kapok seeds, bows, polished
  rock, panning machines and windmill rotors, native gold, alum and ore vessels, sweaters.
- **The simulate clock.** `/sh trade simulate <days>` ages standing's "traded recently" records by
  the days (`StandingLedger.Age`, on `EconomySystem.SimulatedDay`), since it moves the restock clocks
  but not the calendar.

## Maps and leads (#455)

`Trading/Maps/`: `Core/` (`MapPrices.cs`, `MapOffers.cs`, `LeadTargets.cs`, `MapMarks.cs`,
`CampLeads.cs`, `LeadBook.cs`; unit-tested in `tests/Trading/Maps/`), `Game/` (`MapsSystem`,
`MapTradeHooks`, `ItemTraderLead`, `MapOfferAttrs`, `MapMarksSystem`),
`config/trading/map-prices.json`, item `seraphhorizons:traderlead`. Switch `TraderMaps`.

- **Special entries.** A list entry with `"kind"` (`oremap`, `gravelmap`, `lead`) is not goods: at
  each restock `TradingSystem.Offers` (set by `MapsSystem`) expands it into offers, in place in the
  core (`TradeOffers.Expand`, `Trading/Core/TradeOffers.cs`); with no expander (switch off) it is
  left out. Offers marked optional (the settlement lead) give way first when the core is over 16
  slots; the rotating slots shrink to what is left. Every list sells `gravelmap` and `lead`, the
  prospector `oremap` too; their `price` in the list is a placeholder. To keep two rotating slots at
  the top tier (`SchematicTests`), the carpenter's sticks and aged crate, the mechanic's rope and
  metal parts and the smith's tin bronze and steel ingots moved from the core to the rotating pool.
- **Offers.** An offer is an ordinary trade item whose stack carries `offer` and what it is for
  (`MapOfferAttrs`): ore map offers the deposit id, metal, precision and last measured size; leads
  the kind, cell, camp type and position. One ore map offer per metal, the nearest deposit of
  `DepositService.Candidates(x, z, 5000)` that is unsold and not being sold, at most four metals
  (`MapOffers.PickOre`); the gravel map the nearest such field of `GravelFields(x, z, 2000)`. Deposits
  in range but none left: a `soldout` offer with stock 0 (drawn unavailable by the game). The
  `lead` entry expands to the 8 km settlement cell's centre (`LeadTargets.Settlement`) when the
  shelf tier has `mapsToTraders`, and to nothing else: camp leads are not shelf offers.
- **Camp leads** (the user's goal: "you can always buy a map to a trader within some radius that you
  don't already have"; radius and count grow with standing, so learning from local traders is
  cheaper than buying the whole map from one). Per buyer, so off the shelf: the shared shelf has 16
  slots, is stocked with no buyer, and a partner's eight would have crowded out the goods.
  `MapsSystem.CampLeadsFor(player, trader)` builds a `LeadBuyer` and asks `CampLeads.Pick`:
  - *Candidates*: every grid cell whose camp could be within the tier's radius
    (`CampLeads.CellsAround`, one cell more than the reach), its site from the registry (the placed
    camp, else the spot it waits for next; cells with no spot left, and cells a sale found no camp
    in, `_noCamp`, are skipped), distance from the trader. The trader's own cell never.
  - *Have*: the camps the buyer has, `camp:x,z` targets with a remembered live marker at any
    precision (`MapMarksSystem.MarkedKeys`), carried as a lead (`MapMarksSystem.Held`, pending
    ones included) or being drawn for them (`_drawing`).
  - *Tiers* (`map-prices.json` `campLeads.tiers`, by standing tier code; standing off is a
    stranger): known 2 within 3 km, regular 3 within 5, trusted 5 within 8, partner 8 within 12, the
    nearest they lack; if they have no prospector within the radius, the first slot is the nearest
    prospector they lack there (`CampLeadOffer.Prospector`). Recomputed every time, so a lead bought
    is "had" and the next-nearest fills the slot.
  - *A stranger* (a tier not listed): one lead per trader per group, ever (`GroupLeads.StrangerMaps`),
    to the nearest camp they lack that the group has not visited (`GroupLeads.Visited`, fed by
    `EntitySeraphTrader.Met` through `MapsSystem.OnMet` for grid camps), within `strangerReach`
    (8 km). So they chain from camp to camp and cannot map a region from one trader.
  - *Price* (`CampLeadRules.Price`): `base` 2 × 2^(distance / `doublingDistance` 2500) ×
    `perBought` 2 ^ (leads the group bought from this trader, `GroupLeads.Bought`, never reset) ×
    the tier's discount (0.85, 0.7, 0.55, 0.4), rounded, at least 1. The discount replaces
    standing's buy price factor (the economy's modifiers are not applied): one factor for one
    thing, set where the rest of the lead's price is.
  - *The window*: `TradeWindowState.LeadOffers` (cell, type, distance, dx, dz, price, prospector),
    `LeadsWhy` (stranger used, none in reach) and `LeadsBought`; the Maps & leads tab lists them under
    the shelf's offers with a Buy button (`TradeAction.BuyLead`, the cell as `Code`, the price seen
    as `Price`). The Standing tab's tier lines say "maps to n traders within r km"
    (`TierView.LeadMaps`, `LeadRadius`) or, for a stranger, "one map onward".
  - *The buy* (`MapsSystem.BuyCampLead`): refused, nothing taken, when the cell is not among the
    buyer's offers at that price (the why key, or `trading-window-changed`), one of theirs is still
    being drawn, their bags have no room (`TradeWindowSystem.HasRoom`) or they lack the gears. Paid
    from their gears into the trader's wallet, counted by standing as a deal of that many gears; a
    pending lead ("being drawn") goes to their bags at once, and the cell is settled by
    `ResolveCamp` (its next spots' chunks loaded until the camp is placed or none is left, as
    `/sh trade tp` does). Placed: the group's count here goes up (and a stranger's lead is spent),
    and the pending lead becomes the lead to where the camp stands. None: the gears come back from
    the trader, the cell joins `_noCamp` (for the server's run) and the next camp takes its place.
  - *Saved* (`LeadBook`, savegame key `seraphhorizons:leads`, `{"version": 1, "groups": {...}}`):
    per group key `player:<uid>` and `company:<group uid>` (standing's company), maps bought per
    trader id, traders that sold the stranger's lead, camps visited. Written to the player's key and
    their company's, read as the most of them (the higher count, either's stranger lead, the union
    of visits), as standing pools. A newer version than the code knows, or a save that does not
    parse, starts empty with a warning; a world from before has none.
  - *Admin*: `/sh trade leads [player] [trader]`, the history per group key and the trader's offers
    to the player with prices (`--json` too).
  - Leads of the old kinds (`prospector`, `far`) in players' bags still read; old camp lead offers
    left on a shelf until its next restock show sold out and are refused.
- **Per player.** At a restock offers are priced for nobody (precision 1). When the trading player
  changes (`TradingPlayerPriced`), every offer is re-priced and an ore map offer's precision set to
  `MapOffers.MaxPrecision(mapTier)` (0 → 1, 1 → 2, 2+ → 3), times standing's factor through the
  economy's modifiers.
- **The sale** goes through the game's `ITradeableCollectible` (vanilla's locator maps do the same):
  `ItemOreMap` implements it through its static `Hooks` (set to `MapTradeHooks`), `ItemTraderLead`
  directly. `OnTryTrade` (server, before money) refuses a sold-out offer, a deposit sold or reserved
  meanwhile (setting the shelf's stock to 0), an ore map above the buyer's precision, and the
  settlement lead without the buyer's own `mapsToTraders`; it notes the price. `OnDidTrade` gets the stack about
  to be handed over: it is marked pending (a token) and the sale settles. Ore and gravel maps:
  reserve the deposit, `DepositService.Verify` (which may generate up to nine columns), then
  `MapIssuer.Issue` (marks it sold). Settlement leads at once (camp leads: above). If that
  finishes inside the deal, the stack handed over is the map; otherwise the pending stack ("being
  checked") is replaced in the buyer's inventory when it does (handed over anew if it moved), with a
  chat line. A failed sale takes the pending stack back and refunds the gears from the trader.
- **Markers** (`Core/MapMarks.cs`, `Game/MapMarksSystem.cs`, the playtest after the trade window).
  Ore maps, gravel maps and leads put their waypoints on the reader's map through
  `MapMarksSystem.Mark`, which sets the waypoint's `Guid` and remembers it with the target and
  precision (`MarkBook`, saved as `seraphhorizons:mapmarks`; records whose waypoint is gone are
  pruned). Targets: `deposit:<id>` (ore or gravel), the camp's standing id `camp:x,z`,
  `settlement:x,z`. Precision is the ore maps' (1 ±400 m, 2 ±150 m, 3 exact); a gravel map is 3,
  a lead `LeadPrecision` (2: the camp's site, the trader within about `LeadReach`, 64 blocks), a met
  trader 3. Titles carry it: `map-waypoint-precision` "{name} (precision n, ±r m)",
  `map-waypoint-lead` "{name} (approximate, ±64 m)", `map-waypoint-exact` "{name} (exact)". Marking
  a target that has a marker as precise or better adds nothing ("already marked"); a rougher one is
  removed and replaced. An unremembered waypoint with the same icon on the very spot (made before
  this) is adopted. The waypoint layer's private `ResendWaypoints` is called by reflection only to
  remove without adding (`Unmark`); `AddWaypoint` resends anyway.
- **Meeting a trader**: `EntitySeraphTrader.Met` (raised on the server when a conversation starts,
  `EntityBehaviorConversable.OnControllerCreated`, and when the trade window opens). With maps on,
  a trader of a placed camp of the grid (`MapsSystem.CampOf`: the cell's placed camp within
  `StandingSystem.CampReach` of the trader, so it works with standing off too) gets an exact marker
  where it stands, "Trader camp (cook) (exact)", target `camp:x,z`, once. Its lead's remembered
  marker goes by target; an older unremembered one by `MapMarks.LegacyMatches`: icon `trader`,
  within `LegacyReach` (96, the camp reach) of the trader, title starting with the lead's title for
  the type in the player's language or English. Traders outside camps (visitors, story NPCs) are
  not marked: they move on. Delivery markers ("Delivery: cook") are left alone: they are not maps.
- **Maps the player has** (`MapsSystem.Refusal`, `MapMarks.Check`): an offer is refused (and the
  Maps & leads tab greys it out, "you have this", from `TradeWindowState.OwnedMaps`) when its
  target is marked as precisely or more, or the player carries a map or lead of it as precise or
  more (hotbar, backpack, mouse, character; pending stacks too, by their offer attributes). Only a
  copy as precise counts, so a more precise map of a target marked roughly is sold (an upgrade);
  reading it replaces the rougher marker. Ore deposits and gravel fields are sold once anyway, so in
  practice this refused a second lead to the same camp, or one to a camp already marked or met;
  camp leads now leave such camps out of the offers instead.
- **A deposit gone since the restock**: a shelf keeps its offers until the next restock, but a
  gravel or ore cell whose spots were all tried meanwhile has none left (`DepositService.Candidate`
  null). The sale used to take the gears, `Verify` found nothing, and the pending sheet was taken
  back with a refund: in the playtest a gravel map bought after two leads "never arrived" (the chat
  said "fell through: the claim is gone. 5 gears back"; its cell had failed every spot ten minutes
  before). Now `Refusal` checks the candidate before payment ("trading-maps-error-gone") and sets
  the stock to 0, and `Price` (when the trading player changes) zeroes the stock of any deposit
  offer sold, reserved or gone, so the window shows it sold out.
- **Before the deal**: the trade window's buy asks `MapsSystem.Refusal` and the room check
  (`TradeWindowSystem.BeforeBuy`, passed to `EntitySeraphTrader.BuyUnit` as its check) before the
  carts are touched, so a refusal says why (the lang key in the result) and takes nothing.
  `OnTryBuy` asks `Refusal` again inside the deal for vanilla's deal packet.
- **Never bought back**: the economy's `refused` prefixes (`seraphhorizons:oremap`, `gravelmap`,
  `traderlead`, `game:locatormap`) refuse them off-list, and no list buys them
  (`TradingMapsScenarios` checks the quote).
- **Known limits.** The size class priced is the last measurement (unsurveyed until someone verifies
  the deposit); the deal's own verify may find it different, and the map says what it found. A camp
  lead to a pending cell is offered at its waiting spot's distance and price; only the sale settles
  where the camp is. A cell `_noCamp` skips is forgotten at a restart (its spots are all tried
  then, so it is skipped anyway unless a second chance places it later). Settlement grounds are reserved but empty until settlements exist (#468). The pending sheet
  reads as a blank map if the server stops before the sale settles; the deposit is not sold then.

### Edits to shared files (for the integrator)

- `Trading/Game/EntitySeraphTrader.cs`: event `TradeOpened` (raised in `Dialog_DialogTriggers` after
  the standing line); `Restock` resolves at `Standing.ShelfTierFor` with that tier's `rareStock` and
  passes the result through `TradeOffers.Expand(resolved, system.Offers …)` (three lines).
- `Trading/Game/TradingSystem.cs`: property `Offers`.
- `Trading/Game/TradeLists.cs`: `ItemFor` makes the JSON of an entry it did not load (`Json(entry)`,
  split out of `ToJson`).
- `Trading/Core/TradeList.cs`: `TradeEntry.Rare`, `Kind`, `Optional`. `TradeListResolver.Resolve`
  takes `rareStock` (default false). New `Trading/Core/TradeOffers.cs`.
- `Trading/Economy/Game/EconomySystem.cs`: `Reprice` resolves at `MaxStandingTier` with rare stock.
- `Trading/Standing/`: `IStandingSource.ShelfTierFor`, `UnlocksOfTier` (and in `NoStanding`,
  `StandingSystem`); `StandingLedger.Age`.
- `Ore/Game/ItemOreMap.cs`: implements `ITradeableCollectible` through `Hooks`, and asks the hooks
  for an offer's name and description first.
- `TradeCommands.cs`: unchanged.


## Travelling merchants (#456)

`Trading/Visitors/`: `Core/` (`InnRules.cs`, `VisitSchedule.cs`, `VisitConditions.cs`,
`InnEvaluation.cs`; unit-tested in `tests/Trading/Visitors/`), `Game/` (`InnSystem`, ExecuteOrder 0.66,
switch `TravellingMerchants`; `InnProbe`, `BlockInnFlag`, `EntityVisitingTrader`, `InnCommands`). The
player-facing rules are in the mod's README ("Travelling merchants"); what follows is how it hangs
together.

- **Types without camps**: `TraderTypes.Visitors` (`travellingmerchant`, `travellingcurio`) are not in
  `TraderTypes.All`, so the grid, the relations and the schematics never see them. `TradeLists.Load`
  reads their lists with the eleven (so `TradingSystem.Lists` has 13), and `CampWeights` keeps to the
  eleven. `TradeListResolver.Problems` accepts both.
- **Entities**: `make_entities.py` also writes `visitor-{male,female}.json` (code `visitor`, the
  same variant groups, outfits keyed `visitor-*`, the curio dealer in the luxuries set), class
  `SeraphHorizons.VisitingTrader` (`EntityVisitingTrader : EntitySeraphTrader`), without
  `reviveondeath`, `emotionstates` and the melee, seek and flee tasks. `ReceiveDamage` lets only
  healing through. The trader type comes from the code's third part as for the camp traders.
- **Standing id**: set before spawning, `WatchedAttributes["seraphhorizons:traderid"] =
  visitor:<kind>` (`TraderIds.Visitor`, which `IsValid` now accepts), so `StandingSystem.TraderIdOf`
  keeps it and never makes a visitor near a camp the camp's trader.
- **Stock tier**: `EntitySeraphTrader.StockTier` (virtual, 0) is the tier `Restock` and the economy's
  `Reprice` resolve the list at. A visitor's is its inn owner's tier with its kind at arrival (saved
  in its visit attribute), which is how `standingTier` 3 entries are its rare goods. Camp traders
  still stock at tier 0 until standing wires a tier in.
- **Clock**: visits run on `Calendar.TotalDays + InnBook.Offset`; `EconomySystem.SimulatedDay` adds a
  day to the offset and steps every inn, so `/sh trade simulate` moves visits like supply.
- **Lifecycle**: `InnSystem.Update` (every 2 s, and per simulated day) steps each `InnRecord`:
  evaluate an idle inn once per whole day (only while its chunk is loaded), spawn on the arrival day
  (likewise), and on the leave day despawn the visitor if loaded and start the cooldown. Each visitor
  also asks `CheckVisitor` once a second and leaves (`Die(Removed)`) when its record is gone, not
  visiting, or past its leave day, or the system is off; so one unloaded at the end of its visit
  leaves on load. A flag whose block went without `OnBlockRemoved` is noticed when its chunk is
  loaded. A spawn that fails puts the inn into cooldown rather than retrying every tick.
- **Flag owner**: `DoPlaceBlock` (it has the player) records the owner after `OnBlockPlaced` (which
  any `SetBlock` calls, so a flag placed by a command or a test makes an ownerless inn).
- **Light and solidity**: the rules estimate block light from the room's light sources (the game's
  falloff of one per block, Manhattan) rather than reading the light engine, which is the night-time
  light, independent of the hour and of when the engine catches up. A block is solid for the fill
  unless it is air, a liquid, or replaceable from 5000 (plants, snow layers).
- **Supply**: the visitor's specials are sold nowhere, so they have no supply of their own; the
  condition reads the region's levels of the visitor's *buying* list (`BuyerIndex.FullCode`).

Open: visitors only come while the inn's chunk is loaded; the arrival is not announced at the
camps; a world without the grid skips standing entirely (vanilla camps have no standing ids).

## The trade window

The playtest after the overhaul's waves found vanilla's trade dialog the wrong tool for it: carts and
a Deal button for one-off trades, the standing in chat, orders and deliveries by chat command, maps
and leads as odd shelf entries, the side budget squeezed into its money line. The pack's traders
(`EntitySeraphTrader`, and so `EntityVisitingTrader`) now have a window of their own,
`Trading/Window/`: `Core/` (`TradeWindowState.cs`, the wire format; `TradeGuard`, `HoldTimer`,
`LockedStock`, `TradeWindowModel` and `StandingSpeech`, `SellPool`, `WindowLayout` and
`WindowPlacement`; unit-tested in `tests/Trading/Window/`) and `Game/` (`TradeWindowSystem`,
`GuiDialogSeraphTrade`, its elements, `HoldRingRenderer`, `TradeWindowPatches`, `ShiftClick`,
`WindowText`). Vanilla's dialog stays for every other trader (story NPCs,
vanilla worlds' traders): nothing of it is patched any more.

**Opening.** The dialogue's `opentrade` ("Got anything to trade?") is handled in
`EntitySeraphTrader.Dialog_DialogTriggers` (protected virtual, called on both sides). On the server
vanilla's own handling runs as before (alive, reach of 7 squared blocks, one trading player at a
time, `tradingPlayerUID`), then `TradeOpened` and the window's state. On the client vanilla's would
open `GuiDialogTrader` (its private `TryOpenTradeDialog`); ours does what that does with our window:
packet 1001 (the server opens the trader's inventory to the player, so the sell slot syncs), the
inventory opened locally, the window in vanilla's protected `dlg` field and the player in
`interactingWithPlayer`. So vanilla's tick closes it when the player walks off (past 5 squared
blocks) or the trader dies, and its packet 1212 closes it from the server, as with its own dialog;
closing sends 1212 back. Only that option opens the window.

**Inventory.** `SeraphTraderInventory` is vanilla's `InventoryTrader` (same slots, saved and synced
the same way, so every economy patch and list keeps working), set before vanilla makes its own
(`Initialize`, `FromBytes`). Its `ActivateSlot` ignores clicks on the shelves and the buying cart (nothing is
ever put in it by a click) and passes the four selling-cart slots through: those are the window's
sell slots (36 to 39), `ItemSlotSell` (`NewSlot`), which take anything but money and delivery
packages, whether the trader buys it or not (vanilla's `ItemSlotBuying` takes only what it buys). The selling cart belongs to its owner, the trading player when the window opened
(`OwnerUid`; vanilla's `tradingPlayerUID` is cleared by the walk-away tick before the inventory
closes): only they may move stacks in it (slot packets from anyone else are rolled back), sell from
it or send vanilla's deal packet. Closing gives what is left to the owner, at their feet what does
not fit (never into the creative black hole, below), instead of dropping it by the trader; anyone else closing leaves the carts alone; a new owner
returns a previous one's leftovers (or drops them by the trader), and a trader leaving hands them
back. A buy carries the item and price the player saw and is refused if the shelf changed meanwhile;
a deal that throws restores the carts, the rest of a sold stack and the side budget.

**Network.** One channel, `seraphhorizons-trade`, one protobuf message (`TradeWindowPacket`: kind,
trader entity id, JSON). Client to server: a `TradeRequest` (`Refresh`, `Buy` with a selling slot,
`Sell`, `TakeOrder`/`HandInOrder` with an order id, `TakeDelivery`, `HandInDelivery`,
`MarkDelivery` with a delivery id or 0 for the offer). Server to client: a `TradeResult` (done or
refused, a lang key and its arguments, formatted in the player's language) after each, and a
`TradeWindowState` (standing with every tier, orders, the delivery offer or why there is none, the
player's deliveries from or to this trader, locked stock, which features are on) after each, when
the window opens, and when a conversation with the trader starts (for the dialogue). The shelves
are not in it: the client has them from the trader's inventory (packet 1234 and the watched
attribute, as vanilla).

**The server's side** (`TradeWindowSystem.Handle`, callable directly, which the Atlas scenarios do):
every request passes `TradeGuard` (the trader alive, the player its trading player, within 7
squared blocks), then:
- **Buy, one unit, at once** (`EntitySeraphTrader.BuyUnit`): the carts are emptied aside, the unit
  put alone in the buying cart (one trade stack of the shelf slot, with its `ResolvedTradeItem`), and
  vanilla's own `TryBuySell` (internal, by reflection) runs. Everything vanilla's deal did still
  happens, through the same code: money both ways, stock, the wallet check, the economy's patches,
  the map and lead hooks (`ITradeableCollectible.OnTryTrade` and `OnDidTrade`: pending stacks,
  refunds), then standing (`AfterDeal`), `Dealt` (orders count their goods), the nod, and the
  inventory broadcast.
- **Sell, one lot, pooled** (`EntitySeraphTrader.SellLot`, `SellPool`): the four sell slots are valued
  together, each good at its listed or off-list offer's gears per item, a listed good only as many
  as its demand takes. A lot's target is the first good's unit price in whole gears (the dearest
  goods per item first, then by slot), at most the pool's whole gears; items are taken in that order
  until they are worth the target, and the trader pays the floor of what it took is worth, never
  more. The rest stays in the slots: two slots of 20 dirt at 28 to the gear sell one gear's worth,
  where neither alone could. Vanilla's deal sells whole units a cart slot at a time, so this sale
  does its bookkeeping itself, the same as the deal and the economy's patches: the wallet and the
  side budget pay their shares (in proportion to what each budget's goods in the lot are worth;
  either short refuses, `trading-window-trader-broke`), the listed goods' demand drops (a part unit
  counts whole), the gears go to the player (`SeraphTraderInventory.GiveOrDrop`), the goods' trade
  hooks run, then supply (`EconomyPatches.RecordSupply`, shared with the deal's postfix), standing
  and `Dealt` (`Credit`), the nod and the broadcast. Under a whole gear it refuses with the worth so
  far (`trading-window-sell-under`).
- Orders and deliveries call the systems' own handlers; a hand-in takes the order's item from
  anywhere in the hotbar and backpack. Mark on map adds a waypoint as a lead does.
- **Room** (`TradeWindowSystem.HasRoom`, `TradeGuard.Fits`): a buy, and taking a delivery's
  package, is refused before any gears move ("trading-window-noroom") unless the whole stack fits
  in the hotbar and backpack (bags included) as the game would give it: per slot, an empty slot
  that `CanHold` it gives the stack limit (the item's and the slot's), a stack it merges with
  (`GetMergableQuantity`) what that stack lacks to the limit, any other nothing; partial room adds
  up across slots. Vanilla's deal gives what does not fit by dropping it at the player's feet
  (`GiveOrDrop`). The check does not count gears the payment would free (a slot of gears paid
  out to the last one): it refuses then, which errs on the safe side. The client's hold loop stops
  at the first refusal, as at any.

**Holding.** No carts and no Deal button: every trade is one lot, made when a hold completes
(`HoldTimer`, 0.8 s, Carry On's default interact delay). Pressing a good selects it (details below
the shelves); holding the press buys one unit, and holding on buys the next once the server
confirmed the last; a refusal stops it until the button is let go, and moving off the good cancels
it. Selling: goods go in the four sell slots (drag, or shift-click, below), what they fetch shows
under Hold to sell, and each hold sells one lot. The ring (`HoldRingRenderer`) copies Carry On's
hold-to-pick-up ring (its `HudOverlayRenderer`, public domain): light grey, 24 px times the GUI
scale, inner edge at three quarters, sixteen steps clockwise from the top, fading in over 0.2 s and
out over 0.4 s, at the mouse; each confirmed lot flashes it gold and swells it for 0.35 s. Ortho
stage, order 1.05, after the dialogs, at depth 19000. The first version drew at depth 600 and never
showed: the game draws each open dialog further forward (`GuiManager.OnRenderFrameGUI` translates
by every dialog's `ZSize` in turn, with the depth test on), so the trade window under the mouse sat
in front of 600 and hid it. The ortho stage's modelview is at -19849 and its far plane at 20001, so
depth runs to about 19848.

**Shift-click** (`ShiftClick`, Harmony, both sides: the client predicts, the server acts on the
click's packet). The playtest lost a delivery package and a stack of gears shift-clicked with the
window open, in creative. A shift-click runs `PlayerInventoryManager.TryTransferAway`, which offers
the stack to every inventory the player has open and moves it to the best-weighted slot. The
trader's inventory took neither money nor goods it does not buy, and a creative player always has
the creative inventory open (`InventoryPlayerCreative.HasOpened`), whose black hole slot (weight
0.01) deletes what goes in: with no room in the hotbar (no bags), the stack went there. In survival
nothing was lost (an Atlas scenario checks both). Now a shift-click on a hotbar or backpack stack
with the window open moves the whole stack into the first free sell slot, or nothing moves (money,
packages, every sell slot taken); a shift-click out of a sell slot runs the game's transfer, and
that, closing and a sale's gears never feed the black hole (`ShiftClick.KeepOutOfBlackHole`, a
prefix on `InventoryPlayerCreative.GetBestSuitedSlot`).

**Layout** (the playtest's mockup "A · Tabs"; `WindowLayout`, `Flow`). Every block of text is
measured wrapped to its width with the game's own text measuring (`TextDrawUtil`, what the static
texts draw with) and the next row starts below the tallest thing in the one before: the playtest's
fixed heights cut the footer off at the right, ran the Orders intro into the first order with Take
on top, and ran the Standing tab into the footer. The window recomposes when anything it shows
changes, never while a mouse button is down (a drag or a hold would lose its mouse-up). Orders,
Deliveries, the Maps & leads lines and Standing lay out in a scroll area as tall as the screen has
room for. **Placement** (`WindowPlacement`): the minimap is 254 px at the right top (unless moved),
and the coordinates HUD sets its own offset below the first other right-top dialog every 250 ms;
the window is placed with `EnumDialogArea.None` (a right-top window would push the coordinates
below itself) at the right edge, its tab row below the lowest right-top HUD; with no room there it
sits left of them. A local render harness (not in the repository) drew every tab with long content
at several screen sizes and GUI scales with the game's Cairo text code to check it. A header with the trader's name, type and region and
the player's tier with a bar to the next and the raw numbers ("Regular [310 / 800]"); tabs Trade,
Orders (n), Deliveries (n), Maps & leads and Standing; a footer with the player's gears, the
trader's and its side budget ("for goods off her list 21 g"). A feature that is switched off
(`TraderStanding`, `TraderOrders`, `TraderDeliveries`, `TraderMaps`; the side budget with
`EverythingHasAPrice`) has no tab or line. The Trade tab's shelves are the trader's own slots (price
and stock in the tooltip, sold out crossed out as vanilla draws it); map and lead offers are on the
Maps & leads tab instead, with their metal, size, distance and precision, or target and direction,
sold out and locked ones saying why. Locked stock (`LockedStock`): what the selling list would shelve
at the top tier with rare stock, less what the player's own tier gets, less what is on the shelf
anyway, hatched (`GuiElementSlotHatch`) with the tier that unlocks it (its `standingTier`, or the
first tier with `rareStock`). The Standing tab lists the five tiers with their thresholds, the
current one marked, what it gives against the next, and how to earn more.

**Prices shown.** Under Hold to sell, short: "Trader pays 1 g per 28" for the good a hold sells
first, and what everything in the slots comes to, or its worth so far under a whole gear. The
breakdown is in tooltips (`TradeWindowPatches`, `ItemSlot.GetStackDescription` postfix): over that
text, each sell slot (or why it does not sell; such a slot is veiled with "doesn't buy this",
`GuiElementSlotNote`), and, while the window is open, every item in the player's own inventory (the
list's price, or value × spread × fit × supply and which budget pays) or why not; the client prices
from the same synced data as the server (see "Everything has a price").

**The standing in the dialogue.** The pack ships its trader dialogue,
`assets/seraphhorizons/config/dialogue/trader.json`, written by `Trading/tools/make_entities.py` from
the game's (re-run it after a game update): vanilla's, with "How do you see me these days?" second
in the main menu, shown on the condition `entity.shstanding` = `on` (the trader's `variables` tree,
which the server sets while standing is on), answered by the component `seraphhorizons-standing`.
The entities point at it (`dialogueByType`). BetterRuins' two quest dialogues, which the curio
dealer and the farmer use (`patches/trading-betterruins-dialogue.json`), get the same option and
component by patches on their files (component 20, `main`, in BetterRuins 0.6.4; an Atlas scenario
checks every pack trader's dialogue). The game has no variables in dialogue text, and the answer is
written client side when the player picks it, so: the server sends the window's state when a
conversation starts (`EntityBehaviorConversable.OnControllerCreated`, on the right-click), and a
client postfix on `DlgTalkComponent.genText` (protected) replaces that component's text with
`StandingSpeech`: in the trader's voice, generic for every type, with the raw numbers in square
brackets after each line ("You're a regular here now, I'd say. [Regular · 310 points · trusted at
800]"), covering the tier and progress, what the tier gives and the next unlocks, and how to earn
standing. Without a state (a race, or a patch that did not bind) the file's own line stays.

**Decisions.**
- One lot per completed hold, holding on for more, rather than a quantity field: a refusal (money,
  side budget, stock, demand) stops at the lot it hits.
- A buy stays vanilla's deal (`TryBuySell` on the carts, stashed around it) rather than a copy of
  it: the economy, maps and orders already hook into it, and anything another mod hooks there
  applies. A sale is the pooled lot's own (the playtest asked for four sell slots valued together,
  and vanilla's deal only sells whole units from one cart slot), with the deal's bookkeeping: the
  economy's supply recording is the same method, standing and orders the same calls.
- Vanilla's packet 1000 is still honoured for the pack's traders (it credits standing and orders
  as before), though the window never sends it.
- The window is client-only code over a tested view model and a tested layout (`WindowLayout`: no
  box overlaps or runs past the width, with long text); its look in the client has not been checked
  by a test (Atlas has no client, and the client will not start without a login), only its server
  side and the layout, which a local harness also drew with the game's text code.
