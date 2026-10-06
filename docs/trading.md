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
  Our types have neither (see open problems).

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
×0.6, the other way ×1.6), and the pack's own goods are added where the vanilla lists had none:
mechanical power, pipes and steam (mechanic), ore samples and mining supplies by rock group
(prospector), seeds and saplings by climate (farmer), planks and logs by climate (carpenter), stone by
rock group (mason), young animals by climate and tack (animal dealer), everyday supplies (general
store). The tables were written with a one-off script; the JSON is the source.

### Camps on a grid (#447)

- **Grid** (`Trading/Core/TraderGrid.cs`): 2048-block cells (whole chunks). A cell's camp goes at the
  first of 8 seeded spots (≥ 192 from the cell's edge) where a camp fits. Every 8192-block cell is a
  future settlement: no spot within 512 blocks of its centre (a corner shared by four camp cells).
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
  holding a cell's spot whose turn it is tries the camp structures in a seeded order weighted by their
  `chance`, through the game's own `TryGenerate` (reflection), with the climate and forest values
  GenStructures computes: at the spot, then at the chunk's other points in a seeded order (at most 48
  that look flat in the chunk's heightmap). The game's surface placement only takes ground whose
  schematic corners are at one height, which a single point rarely is (vanilla gets there by rolling
  many structures at random points of every chunk); in the test world a spot's chunk took a camp at
  the first or second spot, and records a placed camp as GenStructures does (generated structure of group
  `trader`, land claim). Chunks generate in any order, so each cell's state is saved
  (`CampRegistry`, savegame key `seraphhorizons:tradercamps`): a spot is tried only when every earlier
  spot has missed; a spot whose chunk is generated earlier is passed for good.
- **Traders in camps**: an `onattemptspawnerspawn` listener rewrites any other mod's trader code (by
  class `EntityTrader` or a trader code) into ours: the cell's type, the spawner's gender, the outfit
  set by climate as vanilla's. Spawners keep respawning the camp's trader as before.
- **New worlds only**: whether a world has the grid is decided at its first start with the mod
  (new world and switch `TraderGrid` on) and saved (`seraphhorizons:tradergrid`); an existing world
  keeps vanilla camps and traders. Switching it off later stops the grid (the world's new chunks get
  the game's camps again).
- **Commands** (`Trading/Game/Commands/TradeCommands.cs`, privilege `controlserver`):
  `/sh trade camps [radius]` (cells around the caller or the spawn: id `cellX,cellZ`, type, placed camp
  or the spot it waits for) and `/sh trade tp <id>` (to the camp; a cell not generated yet is
  generated first). `/sh` is made with `GetOrCreate`; `TradeCommands.Trade` is the `trade` node for
  #459 to add to.

## Extension points for later waves

- **Supply** (#450, #451): set `TradingSystem.SupplyGate` (`ISupplyGate.Stock(TraderContext, TradeEntry)`);
  call `EntitySeraphTrader.Restock` when supply changes. Selling sides only.
- **Pricing**: entries carry vanilla prices; a value system replaces `ResolvedTradeItem.Price` in
  `EntitySeraphTrader.Fill` (one place) or adds a field to entries.
- **Standing** (#452, #463): `TradeListDef.WalletFor(tier)`, `TradeListResolver.Resolve(def, region, tier)`;
  per-trader data can live in the entity's `WatchedAttributes` like the region.
- **Everything has a price**: hook `InventoryTrader.GetBuyingConditionsSlot` / `IsTraderInterestedIn`
  (Harmony on our traders only) to accept unlisted goods at a lower price.
- **Orders, deliveries, maps**: dialogue components on the trader (`Dialog_DialogTriggers` is
  protected virtual: override in `EntitySeraphTrader`).
- **Camps**: `TraderCamps.Registry` (placed camps with type, position, region, structure) for maps to
  other traders and deliveries; `TraderGrid.TypeOf`/`Spots` for where camps will be.

## Open problems

- The handbook's and the recipe browser's "sold by" read vanilla-format lists from entity attributes
  (`tradePropsFile`/`tradeProps`); our types have none, so neither shows them, and both still show
  vanilla's traders. Needs either a flattened vanilla-format view per type or the exporter learning
  our format.
- A camp's spots are decided as chunks generate, so two worlds of the same seed explored in different
  orders can end up with a camp at a different spot of the same cell (or none). `/sh trade camps`
  shows what each world did.
- Camps are not placed in cells that are mostly ocean or high ground: `TryGenerate` refuses spots
  above sea level + 15 in climates under 20 °C, as for vanilla camps.
- Villager lists (`villager-*.json`) belong to the story villagers and are left alone.

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
trader's restock does not pass a tier yet (wave 2 standing wires it). Slot pressure decided the
sellers: the issue's mapping put 20+ schematics on the mechanic and carpenter, so the wooden machines
went to the carpenter, the metal gear work to the smith, chest/crate/candle/toymaker/artisan and the
hand crank to the general store, book/alchemist/texture flipper to the curio dealer, and a few plain
core goods (the carpenter's planks, the mechanic's ppex gears and blades, the smith's charcoal, borax
and copper nails) moved to rotating; every list keeps at most 14 core entries at the top tier.
