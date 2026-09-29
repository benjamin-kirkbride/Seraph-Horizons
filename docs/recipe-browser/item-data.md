# Item data in the export

`ItemSection` (`tools/recipe-export/ItemSection.cs` and `tools/recipe-export/Items/`) writes
three sections of the export: `mods`, `items` and `guides`. This page covers what goes into
each field, where the value comes from, and what is best effort. The format itself is
`schema/recipe-export.schema.json`.

All text is English. For the whole fill the exporter switches the process locale to `en`
and then restores the server's own language (`Items/EnglishLocale.cs`). Passing `"en"` to
each lookup would not be enough: many classes override `GetHeldItemName` and call
`Lang.Get` themselves.

Output is deterministic. Mods, items and sources are sorted by code (ordinal), guides by
asset location, and floats are rounded to 4 decimals.

## Which items

`items` holds every collectible that has a page in the survival handbook, plus every code
in the `referenced` set that `RecipeSection` returns. A referenced code outside the
handbook is exported with `handbookVisible: false`. A referenced code that is not
registered is skipped and named in a server log warning. Nothing else is exported.

The handbook is client code, so the server can only rebuild it. The survival mod's
`ModSystemSurvivalHandbook` calls `CollectibleObject.GetHandBookStacks` for every
collectible, and `Items/HandbookRule.cs` copies the engine's version of that method:

1. No code: no page.
2. `attributes.handbook.exclude` is true: no page. `excludeByType` becomes `exclude` per
   variant when the type loads, and patches apply first. Expanded Matter, for example,
   removes the chromium and uranium ingot exclusions, so those ingots are visible in this
   pack while platinum stays hidden.
3. The collectible needs creative inventory tabs, creative inventory stacks, or
   `attributes.handbook.include: true`. Otherwise it has no page.
4. When it has creative inventory stacks (and `handbook.ignoreCreativeInvStacks` is not
   set), each of those stacks gets a page. Otherwise the collectible itself does.

Stacks that carry attributes (lantern materials, clutter types, shields, banners) are
listed under `extra.handbookStacks` of their code, each with its English name. When the
bare code has no name of its own and every variant has the same name, the item takes that
name.

Gaps:

- Classes that override `GetHandBookStacks` are not called, because the method takes an
  `ICoreClientAPI`. The base rule is applied to them instead. In this pack that means
  `ItemPressedMash` and `BlockLantern` (vanilla, which only change which stacks are shown),
  plus a few mod classes: Primitive Survival's tree hollows, MPE gearboxes and gears, and
  two Immersive Woodworking classes. The server log counts them.
- Pages that mods add in client code through `ModSystemSurvivalHandbook.OnInitCustomPages`
  are not seen, and neither are the vanilla meal and pie recipe pages.
- `handbook.isDuplicate` pages exist in the game but are not listed in the overview. They
  are exported as visible.
- Where an item and a block share a code, only one of them can be a key. The first one
  found in the registry wins.

## Item fields

| Field | Source |
|---|---|
| `kind` | `ItemClass` |
| `name` | `GetHeldItemName(new ItemStack(c))`; when that throws or returns nothing, the lang entry `domain:item-path` / `domain:block-path`. When no English entry exists, the game shows the raw key. The export keeps that key and sets `extra.untranslated: true` (about 390 items in this pack, mostly `game:richgravel-*` from Wilderlands Panning and Hardcore Water blocks). |
| `mod` | See below. |
| `description` | `GetItemDescText()` (the `itemdesc-`/`blockdesc-` entry), then each `attributes.handbook.extraSections` entry as `<strong>title</strong>` plus text, then the `-handbooktitle-`/`-handbooktext-` lang entries, joined by blank lines. VTML is kept. Omitted when all of these are empty. The generated parts of a handbook page (recipes, drops, stats) are exported as data instead. |
| `attributes` | See below. |
| `sources` | See below. |

### Which mod

A collectible does not remember the file that defined it. `Items/ModIndex.cs` reads every
`itemtypes/` and `blocktypes/` asset and keys it by `domain:code`. It then maps the asset's
origin to a mod: a zip mod's assets come from the zip or from its unpack folder (named
after the zip), and the base game's asset folders (`assets/game`, `assets/survival`,
`assets/creative`) are named after their mod. A collectible belongs to the type whose code
is the longest dash-separated prefix of its path, so `game:ingot-copper` goes to the
`ingot` type in `assets/survival`, and its mod is `survival`, not `game`.

Gaps: variants that a mod adds to a vanilla type by patching it are credited to the
vanilla type's mod. Collectibles registered from code, with no type file, fall back to the
mod that owns their domain (the mod with that id, otherwise the mod with the most type
files in the domain, otherwise `game`).

### Attributes

A field is written only when it applies. The tests are the same ones the game uses for its
tooltip.

| Field | Written when | Source |
|---|---|---|
| `maxStackSize` | always | `MaxStackSize` |
| `durability` | above 1 | `GetMaxDurability` |
| `tool` | the item has a tool type | `Tool`, lower case |
| `toolTier` | a tool type, mining speeds or attack power above 0.5 | `ToolTier` |
| `requiredMiningTier` | blocks, above 0 | `Block.RequiredMiningTier` |
| `attackPower` | above 0.5 (bare hands) | `GetAttackPower` |
| `materialDensity` | always | `MaterialDensity` (the engine default is 2000; many vanilla types declare 9999) |
| `nutrition` | satiety or health is not 0 | `NutritionProps`: category lower case, satiety, health when not 0 |
| `burn` | burn temperature and duration above 0 | `CombustibleProps` |
| `smelting` | a smelted stack resolves | `CombustibleProps`: melting point, melting duration, `smeltedRatio` as `inputQuantity`, `requiresContainer`, `smeltingType` as `method` (smelt, cook, bake, convert, fire), output stack |
| `storageFlags` | always | `StorageFlags`, lower case names |
| `extra.grinding` | `GrindingProps` | output stack |
| `extra.crushing` | `CrushingProps` | output stack, quantity, hardness tier |
| `extra.transitions` | `TransitionableProps` | type (perish, dry, cure, ...), fresh and transition hours, output, ratio when not 1 |
| `extra.eatenStack` | food leaves something behind | `NutritionProps.EatenStack` |

The values are the collectible's defaults, not those of a particular stack. Overrides that
depend on stack attributes (`GetNutritionProperties`, `GetCombustibleProperties` for
meals, pies and liquids) are not called.

### Sources (best effort)

`Items/SourceIndex.cs` exports only what the data declares:

| Type | From |
|---|---|
| `blockDrop` | `Block.Drops` of every block, when the drop is not the block itself. `quantity` is the drop's `NatFloat` (avg, var); `tool` when the drop needs one. |
| `other`, note `Harvested` | `BlockBehaviorHarvestable` and `BlockBehaviorFruitingBush` harvested stacks (berry bushes, ...) |
| `entityDrop` | `EntityProperties.Drops`, and the `drops` array of any server behavior (the vanilla `harvestable` behavior, note `Harvested`; others get note `Behavior <code>`). Mod patches such as Good Hunting's are applied. |
| `traderSells`, `traderBuys` | The trade list of every entity type that has `tradePropsFile` or `tradeProps`, read the way the game's `TradeHandbookInfo` reads it. `quantity.avg` is the stack size per trade, `price` the average price in gears. `extra.priceVar` and `extra.stock` hold the rest. |

Known missing:

- Drops decided in code: `Block.GetDrops` overrides (crops by growth stage, ore by
  quantity config, grass and tall plants, leaves with tool-dependent code paths), loot
  from ruins' vessels and chests (`lootvessel`, BetterRuins, betterlootplus), panning
  (vanilla, Wilderlands Panning, the panning machine), fishing (vanilla, Primitive
  Survival), beehives, traps, butchering outputs that the Butchering mod computes at
  runtime, quarrying (Stone Quarry) and machine outputs (Vintage Engineering,
  Electrical Progressive).
- Drop chances that depend on world config or player stats (`DropModbyStat`) are not
  applied; the quantity is the declared one.
- Harvestable drops of an entity are credited to every variant separately; the handbook's
  `groupcode` grouping is not used.
- Trader stock that mods fill in code (e.g. random item pools) is not seen.

## mods

Every mod the server loaded (`api.ModLoader.Mods`): the base game's `game` (Essentials),
`survival` and `creative`, every pack mod, and in tests the Atlas bridge. `name`,
`version`, `authors`, `website` and `description` come from the mod info (empty strings
are left out). `side` is the lower-case `EnumAppSide`. `domains` lists item and block
type domains the mod defines that are neither its own id nor another loaded mod's id.
`extra.type` is `code`, `content` or `theme`. Client-only mods never load on a dedicated
server, so they are absent.

## guides

Every `config/handbook/*.json` of every domain, loaded the way the survival mod's
`GuiDialogSurvivalHandbook` loads it: sorted by asset location; `title` translated; `text`
translated only when it is shorter than 255 characters (a lang key), otherwise used as is.
`mod` is the mod whose files hold the page. Tutorial pages and pages added in client code
are not included.

## Proposed schema changes

- `item.untranslated` (bool) and `item.stacks` (array of `$defs/stack`), promoted from
  `extra.untranslated` and `extra.handbookStacks`.
- `itemAttributes.grinding`, `crushing`, `transitions`: processing that is not a recipe
  in any registry.
- `source.stock` (`{avg, var}`) and `source.priceVar` for trades. `source.quantity` is
  ambiguous between "per drop" and "per trade"; a separate `stackSize` for trades would
  make it clearer.
- `source.type: "blockHarvest"` for harvested bushes instead of `other` with a note, and
  `"entityHarvest"` for butchering, which differs from dropping on death.
- `mod.type` (code, content, theme).
