# The export format

One export is one JSON document: everything the pack's server knows about items and
recipes at one commit. The format is defined by
[`schema/recipe-export.schema.json`](../../schema/recipe-export.schema.json) (JSON Schema,
draft 2020-12). [`schema/examples/minimal.json`](../../schema/examples/minimal.json) is a
small export written by hand that shows every shape.

CI validates each export with `tools/site-data` (`validate`), which also checks the rules
below that JSON Schema cannot express.

## Top level

| Field | Content |
|---|---|
| `schemaVersion` | Integer. Bumped on any change a reader could notice. |
| `generator` | Name and version of the export mod. |
| `pack` | Pack id, pack version and game version. |
| `mods` | Every loaded mod, keyed by mod id. The base game's own mods are included. |
| `items` | Items and blocks, keyed by full code. |
| `recipes` | One record per recipe definition, sorted by `id`. |
| `recipeTypes` | One entry per recipe type, keyed by type code. |
| `guides` | Handbook guide pages that are not tied to one item. |
| `variantGroups` | Optional. The pack's own mod's Tidy Variants groups, keyed by group id. |

## Codes

A code is always `domain:path`, lower case, with the domain present: `game:ingot-copper`.
Codes stay in the data everywhere, next to English names, so other languages can be added
later without a new export format.

## Items

`items` holds every collectible that is visible in the handbook, plus everything a recipe
references. The second group has `handbookVisible: false` when the handbook hides it.

An attribute that does not apply is left out. A stick has no `toolTier`.

Processing the schema has no field for yet is in `attributes.extra`: `grinding` (quern) and
`crushing` (pulverizer), each with its `output` stack, and `juicing`, the fruit press, from
the item's `juiceableProperties`. `juicing.output` is the liquid as the game gives it, a
portion with quantity 1 that only names the liquid; the yield is `litresPerItem` litres per
item pressed (an apple gives 0.3125). `pressed` is the mash left in the press and `returned`
what comes back besides (honeycomb gives 5 beeswax). Mash has no `litresPerItem`: what is
left in it rides on the stack, not the item. `distillation`, the still, is on a liquid with
`distillationProps`: `output` is the distilled liquid, again a portion naming it, and `ratio`
the litres out per litre in (apple cider gives 0.1, grain cider and mead 0.05). `liquid` marks
a liquid: the item's `waterTightContainerProps` are `containable`, and `itemsPerLitre` is how
many of it make a litre (100 for a portion, 5 for Expanded Foods' hard lard). A liquid block in
the world (water, 0.001 per litre) is containable only through its portion item, so anything
under one item per litre has no `liquid`.

`sources` lists ways to get the item other than a recipe: block drops, entity drops and
trader stock. Much of this is driven by code in the game, so the list is best effort.

### Values and switches

Five optional fields come from the pack's own mod (`seraphhorizons`, #506, #523); without it
they are all absent, which is valid. They are optional additions, so `schemaVersion` stayed 1.

- `value`: the item's base value in rusty gears (a rusty gear is 1), the table
  `seraphhorizons:config/item-values.json`'s `values[code]`, as loaded by the server. Only a
  value of the code's own: the mod's family fallback (a missing variant priced as the average
  of its family) is not exported, so an item without one has no `value`.
- `floorZero`: `true` when the table lists the code as worth under a gear per full stack
  (traders take it for nothing). Present only then, and only with a `value`.
- `valuePerLitre`: `true` when `value` is in rusty gears per litre, not per item: a liquid
  the table prices by the litre (its `perLitre`, `{code: itemsPerLitre}`). A portion is then
  worth `value / itemsPerLitre`. Present only then, and only with a `value`.
- `valueSwitches`: the table's `switches[code]`, the config switches the value exists only
  with, because its cheapest route takes a recipe or an item one of them adds. With any of
  them off, the handbook shows "No trade value". The value itself is always the one for the
  default config.
- `switch`: the config switch that adds the item itself (the gear cutter's blocks and parts,
  the gear blanks and their molds, the pickling tub, the bare steel gear, the debarked trunks,
  ...). With it off the item does not exist.

Recipes have `switch` too: the switch that adds the recipe (its file is one the switch leaves
out when off, its type is one only that feature has, such as `picklingtub`, `lottery` and
`gearcutter`, or it is keyed by an item the switch adds, such as the bare steel gear's flash
rust). Which switch owns what is the mod's switch ownership registry (its README, "Switch
ownership"), read by reflection. A switch is named as in `ModConfig/seraphhorizons.json`.

### Variant groups

`variantGroups`, an optional top-level object, holds the variants the game shows as one, so the
site can collapse the same items. Most come from the pack's own mod: its Tidy Variants groups
(#252), the variants the game collapses into one creative tile and one handbook group (every
gravel, every plank wood). The items those leave are grouped as the handbook groups their pages,
by the collectible's shipped `handbook.groupBy` pattern (juice is in creative only inside a bucket,
so Tidy Variants never sees the juice items, but `juiceportion-*` makes one page of them). Like
the fields above it is an optional addition, and `schemaVersion` stayed 1. It is absent when
neither source gives a group (without the mod only the handbook's remain); every reader copes,
and older exports lack it.

```json
"variantGroups": {
  "auto:game:plank": { "title": "Plank", "members": ["game:plank-oak", "game:plank-birch"] }
}
```

- The key is the group's id as the engine gives it: `auto:<domain>:<base>[/<dim>=<value>...]`
  for an automatic group, an override rule's id, or `groupby:<block|item>:<domain>:<pattern>`
  for one merged by a shipped handbook `groupBy` (the mod's `TidyVariants/Core/README.md`); or
  `handbook:<domain>:<pattern>` for a group the handbook's `groupBy` makes among the items left,
  the domain being the leading item's.
- `title`: the group's English title as the game shows it (`GroupTitles.Of`): its lang title,
  else one derived from the members' names, else the representative's name. A handbook group's
  is derived from the members' names the same way (`TitleDeriver.Derive`), else the leader's.
- `members`: codes of `items`, best representative first (the engine ranks every member). The
  entries of one collectible's attribute stacks share a code and count once; a group left with
  fewer than two codes is not exported, nor is a member whose code `items` holds as the other
  kind (a block and an item can share a code). Entries Tidy Variants hides are in no group. A
  handbook group's leader is the first item in code order that declares the pattern, and the
  rest, blocks and items alike as the handbook matches codes, follow in code order.

The exporter reads the server's resolution (`TidyVariantsModSystem.ForSide(Server)`) by
reflection, as it reads the switches; the server resolves at the WorldReady run phase, before
the export runs. The handbook pass matches with the game's own `WildcardUtil`, over the shipped
patterns (Tidy Variants rewrites `groupBy` on the client only), and skips patterns with a `{`
placeholder, which the game expands per stack for clutter and shields, both in Tidy Variants'
groups anyway.

## Recipes

A recipe record describes the **definition**, as the mod author wrote it, and lists the
resolved forms under `variants`.

- `ingredients` and `outputs` are the definition. Codes here may contain `*` wildcards and
  `{name}` placeholders.
- `variants` are concrete. Each has one list of accepted stacks per ingredient, in the
  same order as `ingredients`, and concrete outputs. `bindings` gives the wildcard values
  of that variant, for example `{"wood": "oak"}`.
- A recipe without wildcards has exactly one variant.

`id` is `<type>|<source asset>|<index in that asset>`. It is unique, and stable between
two exports of the same pack, so versions can be compared.

`mod` is the mod whose asset defines the recipe, which is not always the mod that owns
the output item.

### Type-specific data

`recipeTypes[type].shape` says which block a record of that type carries:

| Shape | Block | Used by |
|---|---|---|
| `grid` | `grid`: width, height, shapeless, pattern | Crafting grid |
| `voxels` | `voxels`: layers of rows, `#` filled and `_` empty | Smithing, knapping, clayforming |
| `barrel` | `barrel`: sealing time | Barrel |
| `alloy` | `alloy`; ratios are on the ingredients | Alloying |
| `cooking` | `cooking`; slot quantities are on the ingredients | Cooking |
| `construction` | `construction`: stages, each listing the ingredients it consumes | Blocks built in place |
| `butchery` | `butchery`: stages with what each needs and gives, yields per variant | The Butchering mod |
| `transition` | `transition`: kind, fresh hours and transition hours | Items that turn into others over time: drying, curing, perishing, ..., and the smoking rack |
| `tub` | `tub`: kind, hours, batch size, litres, and how gears are lost | The pickling tub's acids and brine bath (seraphhorizons) |
| `lottery` | `lottery`: when it is decided, and outcomes with chances | The oiled gear (seraphhorizons) |
| `machine` | `machine`: power, shaft turns, work, kept parts, wear and oil | The gear cutter (seraphhorizons) |
| `generic` | none | Every type without a dedicated serialiser, and casting in tool molds |

In a grid pattern each character is the `key` of an ingredient and `_` is an empty cell.

### Blocks built in place

Some blocks are not crafted whole: the player places the block, then right-clicks it with
the items of each stage in their hotbar until it is complete. The water wheel works this
way, and so do ppex's pump, boilers and engines. These are records of type `construction`.
Their `outputs` is the block, `ingredients` lists every stack a stage consumes, and
`construction.stages` says which stage consumes which ingredient. The first stage is the
block as placed and consumes nothing. A stage whose right-click is not "Construct" (the
water wheel's last stage, "Launch") has an `action`.

A stage can remember the variant of what it consumed (`extra.storeWildCard` on the
ingredient) and a later stage can ask for the same variant with a `{name}` placeholder:
the water wheel's planks are of the wood of its support beams. Each value is a variant with
a binding, as for recipes.

### Butchery

With the Butchering mod a dead creature is picked up whole, skinned on a hook, left there to
bleed out, and butchered on a table; or it is harvested where it lies for less. Records of
type `butchery` are one creature type and one carcass item. `ingredients` are the carcass in
each state (role `carcass`), the stations (role `station`, with each block's yield
multiplier in `extra.efficiency`) and the tools; `outputs` are everything any stage gives.
`butchery.stages` lists, per `step` (`pickUp`, `skin`, `bleed`, `butcher`, `harvest`), the
ingredients it needs, `options` it needs one of (knife or cleaver), `optional` ones (a
bucket), `hours` it takes and the outputs it gives. `harvest` is the alternative to the
others, with the mod's cut in `multiplier`, already applied.

Variants are the creatures that give the same. `butchery.variants`, aligned with
`variants`, names them (`entities`) and gives each output's `yields`: average and spread,
or null when that variant does not give it. A variant's `outputs` stacks are the outputs it
yields, with the average as quantity. An output's `extra.scaledBy` says what multiplies it
in game (`efficiency`, `condition`), `extra.needs` the optional ingredients it needs, and
`extra.alternatives` other items it may be instead (carcasses of another coat).
`butchery.condition` is the range of the creature's condition (`min`, `max`; the game's
`animalWeight`), which multiplies the outputs scaled by `condition`.

The `butchery` shape and block are optional additions, so `schemaVersion` stayed 1; so is
`condition`, which older exports lack.

### Transitions over time

Some items turn into others by themselves after a while, wherever they are kept: wet sinew
cures into dry sinew, a raw bowstave dries, raw cheese ripens, snow melts, hot glue
hardens, food perishes into rot. This is the collectible's `transitionableProps` in game.
Each entry is a record of shape `transition`, one recipe type per kind: `perishing`,
`drying`, `curing`, `ripening`, `melting`, `hardening`, `burning` and `converting`. The
record's one ingredient is the item, its one output what it becomes, with the transition
ratio (stacks out per stack in) as the output's quantity: four rot from a raw cheese. The
`transition` block has the kind (`type`, the engine's name in lower case: `perish`, `dry`,
`cure`, ...), `freshHours`, the in-game hours before it starts, and `transitionHours`, the
hours it then takes; each is an average with an optional spread (`var`), from which every
stack draws its own. The item turns when both have passed.

The `id` is `<type>|<item code>|<position in the item's list>`. `mod` and `source` are the
file the entry comes from: the JSON patch that added it when a mod patched it in (Expanded
Foods' dry-aging of vanilla meat is `expandedfoods`, source `game:patches/poultry.json`),
else the item's type file. How that is worked out, and where it can be wrong, is in
[exporter.md](exporter.md#which-mod-a-transition-belongs-to).

A transition can need a station. The Butchering mod's smoking rack turns raw meat into
smoked meat in 4 hours over a burning firepit: records of type `smoking`, whose first
ingredient is the meat and whose second, role `station`, is the rack (not consumed; every
rack block in the variant), with `transition.type` `smoke` and the firepit in
`requirements`. The first ingredient of a `transition` record is always what turns; any
others are stations.

The `transition` shape and block, and the stations, are optional additions, so
`schemaVersion` stayed 1.
Items used to carry the same entries in `attributes.extra.transitions`; that copy was
dropped with no bump, since readers ignore `extra` ([deploy.md](deploy.md#changing-the-export-format)).

### The gear chain

The pack's own mod reclaims rusty gears, cuts new ones and rusts them back into money
(epic #484). Three of its processes have shapes of their own; the cooking pot and barrel
steps are ordinary `cooking` and `barrel` records.

**The pickling tub** (type `picklingtub`, shape `tub`): one record per rule of the tub's
table (`PicklingTubSettings`), id `picklingtub|<gear>|<liquid pattern>`. The ingredients are
the gear (role `batch`), the liquid (role `liquid`; its `litres` are what a finished batch
uses up; the code may be a pattern, and the variant lists every liquid it matches) and the
tub (role `station`). The first output is what the batch becomes. `tub` has the `kind`
(`pickle` for an acid, `rust` for brine), the `hours` to done and the `batchSize`; when the
liquid can lose gears, `failure` is the index of the output a lost gear becomes (steel bits),
`graceHours` and `lossEveryHours` say when an acid starts eating a batch left past done and
how fast, and `lossChance` is each gear's chance to come out lost at done (brine's
over-rusting).

**The oiled gear** (type `lottery`, shape `lottery`): an item decided by chance, one at a time.
The one ingredient is the item; `lottery.trigger` says when (`inventory`: when it lands in a
player's inventory) and `lottery.outcomes` lists each outcome's `chance` and the `outputs`
indices it gives (none: it is lost). Chances add up to 1.

**The gear cutter** (type `gearcutter`, shape `machine`): one record per blank size, id
`gearcutter|<blank>|0`. The ingredients are the blank (consumed), the master (listed in
`machine.kept`: fitted, never consumed), the cutter kit (`isTool`, its `toolDurabilityCost`
the wear per gear with a full oil tank), the oil (each listed oil in the variant, with the
litres one gear drains) and the machine (role `station`). `machine` has the `power`
(`mechanical`), the `turns` of the input shaft one gear takes, the `work` it is made of (12
teeth, 12 turns each), `kept`, `wear` (`dividedByOilFill`: the kit's wear is divided by the
tank's fill, so a dry tank breaks it) and `oil` (`points` drained per gear from a `tank`,
100 points to the litre). Until the cutter's own blocks and kit are registered, their slots
in the variant are empty.

**The draw bench** (type `drawbench`, shape `machine`): one record per metal, id
`drawbench|game:chutesection-<metal>|0`. The ingredients are the hollow section (the game's chute
section, consumed), the fitted parts (the Jonas gearbox, chain, bracket and rod, in `machine.kept`),
the die (`isTool`, its `toolDurabilityCost` one per hollow; an iron die draws lead only, a steel one
lead and copper), the oil (each listed oil in the variant, with the litres one hollow drains) and the
machine (role `station`); the output is four pipe sections of the metal
(`seraphhorizons:pipesection-<metal>`). `machine` has `power` (`mechanical`), the `turns` of the
input shaft one hollow takes, the `work` it is made of (4 sections, `turnsPerUnit` each), `kept`,
`wear` (`fixed`: one point per hollow) and `oil` (`points` drained per hollow from a `tank`, 2 a
pipe section).

**The press brake** (type `pressbrake`, shape `machine`): one record per plate metal, id
`pressbrake|game:metalplate-<metal>|0`. The ingredients are the plate (consumed), the screws and
the edges (both in `machine.kept`) and the machine (role `station`); the output is one angle
(`seraphhorizons:angle-<metal>`), the plate bent once. It is worked by hand: `power` is `hand` and `turns` the turns of its lever clock one
plate takes (a turn a second while the player holds right-click); there is no `wear` and no `oil`.

**The squaring shear** (type `squaringshear`, shape `machine`): one record per plate metal, id
`squaringshear|game:metalplate-<metal>|0`. The ingredients are the plate (consumed), the blades and
the gauge (both in `machine.kept`) and the machine (role `station`); the output is two half plates
(`seraphhorizons:halfplate-<metal>`), the plate cut once across its middle. It is worked by hand:
`power` is `hand`, `turns` the strokes of its treadle clock one plate takes (a stroke a second while
the player holds right-click), and `work` says so (`amount` that many, `unit` `strokes`, no
`turnsPerUnit`); there is no `wear` and no `oil`.

**The mandrel forging station** (type `mandrelstation`, shape `machine`): one record per hollow metal,
id `mandrelstation|game:chutesection-<metal>|0`. The ingredients are the hollow section (consumed), the
mandrel (a rod, in `machine.kept`), the hammer (`isTool`, its `toolDurabilityCost` the blows a hollow
times the wear a blow; `machine.wear` names it, rule `fixed`) and the machine (role `station`); the
output is two pipe sections (`seraphhorizons:pipesection-<metal>`). It is worked by hand: `power` is
`hand`, `turns` the hammer blows one hollow takes with the base hammer (the copper one, the definition's
hammer), each a right-click, and `work` says so (`amount` that many, `unit` `blows`, no `turnsPerUnit`);
a hammer of a higher tool tier forges in proportionally fewer blows, which the record does not carry.
There is no `oil`.

**Casting** (type `casting`, shape `generic`): every tool mold, the game's and the pack's
gear blank molds alike. One record per mold, its colours together (the mold ingredient, role
`station`, has the colour as `*`), with a variant per metal that casts (binding `metal`).
The metal is its ingot, `game:ingot-*`, the quantity in ingots and the units in
`extra.units`; the outputs are the mold's drops with `{metal}` in their code;
`requirements` says to pour it from a crucible.

These shapes and blocks are optional additions, so `schemaVersion` stayed 1: an export that
lacks them is still valid, and the site falls back to the generic card for a record of a
shape it has no layout for.

## Rules beyond the schema

- Every recipe `type` is a key of `recipeTypes`, and `count` equals the number of records.
- Every `mod` on an item or recipe is a key of `mods`.
- Every code in a variant is a key of `items`.
- Recipe ids are unique and sorted by UTF-16 code unit (ordinal order).
- An item's `value` is a finite number, 0 or more.
- Every member of a `variantGroups` entry is a key of `items`, a group has two or more
  distinct members and a non-empty title, and no code is in two groups.
- `variants[].ingredients` is as long as `ingredients`.
- A grid pattern has `height` rows of `width` characters and uses only keys that exist.
- Each ingredient of a `construction` record is consumed by exactly one stage.
- Each ingredient and each output of a `butchery` record belongs to exactly one stage;
  `butchery.variants` is as long as `variants`, each one's `yields` as long as `outputs`, and
  a variant's output stacks are the outputs it yields (with their alternatives).
- A record with a `transition` block has one output and one variant; its first ingredient
  is what turns and every other one has role `station`.
- A `tub` record has exactly one ingredient of each role `batch`, `liquid` and `station`;
  `tub.failure` is an index into `outputs`, present whenever gears can be lost
  (`lossEveryHours` or `lossChance` above 0), and the outputs are what the batch becomes
  plus, with a failure, what a lost gear becomes.
- A `lottery` record has one ingredient; its outcomes' chances add up to 1, and each output
  belongs to exactly one outcome.
- A `machine` record has an ingredient with role `station`; `kept`, `wear.ingredient` and
  `oil.ingredient` name different ingredients, a kept one is not a tool and the worn one is.

## `extra`

Most records have an optional `extra` object for data that has no field yet. Readers
ignore what they do not know. A field that proves useful moves out of `extra` into the
schema with a version bump.

## Later uses

The format is meant to serve a progression graph, a calculator and diffs between pack
versions as well as lookup. That is why recipes have stable ids, quantities are numbers
and not display text, every ingredient and output is a code that is a key of `items`, and
tools are marked as not consumed.

## Changing the format

See [deploy.md](deploy.md) for how to bump `schemaVersion` and add a migration.
