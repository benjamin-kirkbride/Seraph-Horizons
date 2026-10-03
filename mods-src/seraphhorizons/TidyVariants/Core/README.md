# Tidy Variants rule engine (`Core/`)

The game-independent part of Tidy Variants, a Seraph Horizons feature (#252, #255): pure functions over plain data, BCL only,
unit-tested by `dotnet test mods-src/seraphhorizons/tests`. The game layer reads the creative inventory,
the `game:worldproperties` lists and the override file, calls `TidyEngine.Resolve` once at load, and
then builds creative grids, handbook attributes and the Atlas report from the result.

## Flow

```
entries (creative order) + worldproperties + OverrideFile
  └─ TidyEngine.Resolve ─► TidyResolution   (families, classification, hide rule, groups, representatives, issues)
       ├─ DisplayListBuilder.Build(surviving, expanded)  per search keystroke, allocation-free once warm
       ├─ Handbook.Build                                  groupBy pattern / exclude per collectible
       └─ TidyStats.Compute                               numbers for the Atlas report (#259)
```

## Families and classification

A **family** is the entries of one collectible type: same domain, **kind**, base code and dimension names
(a block and an item may share a code: vanilla's ore block `ore-{grade}-{type}-{rock}` and ore item
`ore-{grade}-{ore}-{rock}`; they are always different families, groups and handbook collectibles).
The base code is the code path minus `-v1-v2...` built from the entry's variant map; an entry whose
code doesn't end that way is its own family (`variant-mismatch` issue). Attribute-stack entries add one
dimension per exposed attribute, named `attr:<key>` (for `attr:FSAttributes.wood` the name part after
the last dot is what name rules see).

Each family dimension is classified once from the distinct values its entries take, first match wins.
Values are compared **normalised**: leading/trailing characters that aren't letters or digits are dropped
(Tankards and Goblets' `oak.`), also when looking up preferred values.

1. `loadFromProperties` hint (if the game layer has one) naming a material list, or an orientation list.
2. **Open/closed**: every value in `open opened closed shut ajar`.
3. **Orientation**: every value a compass word (`north east south west up down`) or a letter combination
   of `n e s w u d` without repeats (`ns`, `we`, `nesw`).
4. **Material**: the best of rock, wood, metal, color where at least **60%** of the distinct values are in
   that list and at least min(2, distinct values) match (so a two-value dimension needs both); color
   needs at least **3** matches since color words are generic. Lists: rock = `block/rock` +
   `block/rockwithdeposit`; wood = `block/wood` + `aged veryaged`; metal = `block/metal` + `block/toolmetal`;
   color = built-in dye/cloth/clay colors (vanilla has no color list). Ties go rock, wood, metal, color.
   Then **mixed materials**: the same thresholds over rock + wood + metal + the generic material words
   (`wood stone metal bone clay glass leather cloth reed horn hide`) together, named after the kind with most
   matches (stone and metal tool heads `axehead-{material}`, `beam-plane` in wood, clay and metal, rails in
   `wood`/`metal`).
   Then the override file's **named lists** (`lists`, e.g. `pigment`), same 60% / min(2, n) rule: the
   dimension is `Filler` with `DimensionInfo.List` set, and the list's `preferred` picks representatives.
5. **Process state**: at least 60% (and min(2, n)) of the values are process words (raw, cooked,
   partbaked, charred, perfect, lit, extinct, burnedout, fired, dried, cured, free, snow, grown, ...),
   end in `partbaked charred cooked burned burnt`, or are a process word plus digits (`bake3`).
6. **Grade/size/quality/level**: two or more values, at least 60% (and two) of them in `poor medium rich
   bountiful tiny small large huge none verylow low high veryhigh sparse verysparse dense verydense normal
   thin thick light heavy` (termite mounds' `size[medium,large]`, soil's `fertility[verylow..compost,high]`
   and `grasscoverage[none,verysparse,sparse,normal]`).
7. Names: `rot rotation facing orientation horizontalorientation verticalorientation` are orientation;
   `side direction dir v h updown attach face axis ...` are orientation only when every value is an
   orientation word (compass, `left right top bottom front back ...`); `stage growthstage cover age ...`
   are process state; `state states condition` are process state only if some value is a process value
   (slidingwoodenshutters' `state[left,half,right,...]` is a shape and stays meaningful; its open/closed
   dimension `status[opened,closed]` is caught by value in step 2); `grade size quality fertility coverage
   grasscoverage density thickness fullness level amount` are grade.
8. **Numbers**: every value only digits (`forestfloor-{grass}` 0..7, Butchering's `texture` 1..10,
   `coverage`, `layer`) is filler (`Filler`, reason "values are numbers"); after the names, so `rotation[0,90,...]`
   stays orientation and `stage[1,2,3]` process state.
9. **Attributes** (`attr:<key>`) that nothing above caught are filler: one collectible's creative stacks are
   one tile (bookshelf shapes, bucket contents, fruit tree types). The handbook can only group whole
   collectibles, so splitting them would only make a `groupby-conflict`. An override `split` makes one
   meaningful again.
10. Otherwise **meaningful** (generic `type`, `style`, `ore`, `construction`, ...). When values differ only in
    their numbers (`Vocabulary.NumberStem`: each `-`/`/` token's trailing digits dropped, a size like `2x1`
    kept: `collapsed1..4`, `ruined-barred1..3`, `tier1..3`, `mk1..3`), `DimensionInfo.ByStem` is set and the
    stem, not the value, splits groups (`GroupValue`).

Which `type`-like dimensions are kinds of one thing (paintings, flowers, garments, creatures) and which
are different things (ores, tools, foods, potions, door styles) can't be told from codes: names are only
in the game layer and differ per language. Those are override rules (`tidyvariants-overrides.json`,
"Catalogues of one kind of thing").

Every list is in `Vocabulary.cs`. Filler = every class except meaningful.

## Hide rule

Within a family, entries that agree on every non-orientation, non-open/closed dimension (and the
attribute stack) keep one **canonical** member: the one whose orientation/open-closed values rank best
in the `orientation` / `openclosed` preferred lists (`up north ns n u down east south west`, `closed shut`),
else the first in creative order. The rest are hidden. When the game already lists one orientation
(torches, chests), nothing is hidden. Override `hide` rules hide outright or protect from the rule.

## Groups and representatives

Each visible entry is in exactly one group. The first matching `group`/`ungroup` rule wins; otherwise the
automatic group is the **base code** (domain, kind, base path) plus the names and (group) values of the
entry's meaningful dimensions; all filler collapse. Keyed by base, not family, so families of one base
with different dimension sets share groups when their meaningful values agree (Purposeful Storage's wooden
and stone sword pedestal), and `-` tokens of the base that are process words (after the first) are dropped
(`TidyEngine.GroupBase`: `termitemound-harvested` groups with `termitemound`). A family whose codes don't
end with their variant values (`variant-mismatch`) keeps its own groups.

**Shipped `groupBy`** (`CreativeEntry.ShippedGroupBy`: the collectible's own `attributes.handbook.groupBy`
as vanilla or its mod ships it; `{dim}` placeholders are filled from the variant, a `domain:` prefix is
honoured): ranked below overrides and above dimension derivation. Every automatic entry of the same
domain and kind whose code path a shipped pattern matches is merged (union-find) into one group, so we
never split a page the game already groups (ExpandedFoods `breadedvegetable-*`, dough, flowerpots). A
merge that changes nothing keeps the automatic group; a real merge makes a `GroupSource.Shipped` group
with id `groupby:<block|item>:<domain>:<pattern>`. Override-claimed and hidden entries never take part.
Switch: `ResolveOptions.HonorShippedGroupBy`, else the file's `honorShippedGroupBy`, else true. A
group of one is a plain entry (`GroupOf == -1`), as are ungrouped and hidden entries. Groups are ordered
by first member; automatic ids are `auto:<domain>:<base>[/<dim>=<value>...]` (the stem for a `ByStem` dimension; a clash, e.g. a block and an
item with the same base and values, gets `#2`).

The representative is: the group rule's `representative` match, else a `prefer` match, else the member
whose filler values rank best lexicographically in dimension order (rock `granite`, wood `oak`, metal
`copper`, process `cooked perfect baked bread fired lit ...`, grade `medium`), else creative order. Every
member gets a rank so the display list can pick the best among a search subset in O(1).

## Titles

Only override groups can carry a lang title (`TidyGroup.Title`). For the rest the game layer
(`Game/GroupTitles.cs`) asks `TitleDeriver.Derive` for a title from the members' display names: the
words at least **80%** of the distinct names share (case-insensitive; a word inside brackets counts apart
from the same word outside), in the order of the name that holds most of them (the representative's on
ties). A bracket stays only if every word in it is shared; separators (`:` `,` `;` a lone dash) and
connectives (`of`, `with`, `de`, `aus`, ...) left at an edge by a dropped word go; the first letter is
upper-cased. It returns null, and the representative's name is shown, when nothing but connectives,
brackets or digits is shared, or when the shared words only modify a varying head: in a head-last
language (English, German, ...; `IsHeadLast(locale)`) a varying word right after the last shared one in
the same clause ("Dead clownfish", "Dead carp (adult)": "Dead" is no title), mirrored for head-first
languages (French, Spanish, ...). Names that look like untranslated codes are ignored. Big groups it
cannot name get an override rule with a `title` instead (the Atlas report lists the largest fallbacks).

## Override file

JSON with `//` comments and trailing commas allowed. Unknown fields anywhere are errors; every problem
is reported at once as `rules[3].match: ...` in `OverrideFormatException.Errors`.

```jsonc
{
  "version": 1,                                   // optional; must be 1
  "note": "free text",                            // optional

  // Replace a default preferred list. Keys: rock wood metal color process grade orientation openclosed filler.
  "preferred": { "rock": ["granite", "basalt"] },

  // Extend material lists: more worldproperties files (domain:path under worldproperties/) and values.
  "materials": { "color": { "properties": ["doorvariants:block/door-bricks"], "values": ["seafoam"] } },

  // Named value lists: dimensions whose values are in one are filler (Filler, List = name). values and/or
  // properties are required; preferred is optional. Names can't be built-in class or preference names.
  "lists": { "claycolor": { "values": ["blue", "fire", "red", "tan", "clinker"], "preferred": ["red"] } },

  // Merge automatic groups joined by shipped handbook groupBy (default true).
  "honorShippedGroupBy": true,

  "rules": [
    // match: domain (exact or * wildcard; asset domain, never modid) and code (* wildcard over the path,
    // or an array of them: any matches) are required; kind (block|item) and attributes (name -> value
    // wildcard; only stacks match) are optional.
    // Each rule has exactly one action, plus an optional "note".

    // group: matches form one group, or one per value tuple of "by" (variant names or attr:<key>; a bare
    // name also finds an attribute). id/title may use {dim} placeholders, each listed in "by"; ids must
    // be unique. {base} (the family's base code path, e.g. "breadedball") needs no "by": using it in id or
    // title splits the matches into one group per base code, so `"code": "*"` + `"id": "ef-{base}"` gives
    // one group per type. representative: a code wildcard string, or a match object (domain optional).
    { "match": { "domain": "game", "code": "shield-*" },
      "group": { "id": "shield-{construction}", "by": ["construction"],
                 "title": "seraphhorizons:tidyvariants-group-shield-{construction}", "representative": "shield-*-oak" } },
    { "match": { "domain": "game", "code": "clutter", "attributes": { "type": "book-*" } },
      "group": { "id": "clutter-books", "title": "seraphhorizons:tidyvariants-group-clutter-books" } },

    // Cross-type group (several code patterns) and a whole attribute-stack family in one tile.
    { "match": { "domain": "game", "code": ["amethyst-*", "clearquartz-*", "rosequartz-*", "smokyquartz-*"] },
      "group": { "id": "quartz", "title": "seraphhorizons:tidyvariants-group-game-quartz" } },
    { "match": { "domain": "game", "code": "clutter", "kind": "block" }, "group": { "id": "clutter" } },

    { "match": { "domain": "game", "code": "rock-*", "kind": "block" }, "ungroup": true },

    // dimensions: class per dimension for matching entries: meaningful filler rock wood metal color
    // process grade orientation openclosed, or a name from "lists". split: [dims] is sugar for "meaningful".
    { "match": { "domain": "alchemy", "code": "herbrackmold-*" }, "dimensions": { "clay": "claycolor" } },
    { "match": { "domain": "expandedfoods", "code": "foodoilportion-*" }, "dimensions": { "type": "filler" } },
    { "match": { "domain": "game", "code": "ore-*" }, "split": ["grade"] },

    { "match": { "domain": "game", "code": "creativeonly-*" }, "hide": true },   // hide
    { "match": { "domain": "game", "code": "chest-*" }, "hide": false },         // never hide

    { "match": { "domain": "game", "code": "ingot-iron" }, "prefer": true }      // representative preference
  ]
}
```

First match wins **within each kind**: group/ungroup, hide/unhide, prefer, and per dimension for
dimensions/split. Rules that match nothing are reported (`rule-unused`).

## Display list

`DisplayListBuilder.Build(surviving, expanded)` takes the entries that passed the search filter, in
display order, and the set of expanded group indices. Hidden entries are dropped; each group with two or
more surviving members becomes one tile (representative = best-ranked surviving member) at the position
of its first surviving member; an expanded group is emitted inline as its surviving members, flagged
`ExpandedMember` with `GroupStart`/`GroupEnd`; if exactly one multi-member group survives it is
auto-expanded (`AutoExpanded`, `AutoExpandedGroup`); a group with one survivor is a plain entry.

## Handbook

`Handbook.Build(res, verifyAcrossKinds = false)` gives, per collectible (kind + code), one `groupBy` pattern over
the code path (no domain), the first exact one of: a `*` wildcard where varying variant positions become `*`
(`ore-*-nativecopper-*`); the members' common prefix `*` common suffix; then, except for groups with attribute
stacks, the shorter exact one of two `@` regexes (`GroupByRegex`): **structured** (per `-` position the value or an
alternation of the members' values, positions after the first relaxed to `[^-]*` while still exact:
`@hide-raw-[^-]*`) and **enumerated** (exactly the members' codes as a token trie). Exact means: with the game's
matcher (`GroupByMatcher`: `@` = regex wrapped in `^…$`, case-sensitive; else `*` wildcard ignoring case), it matches
every member and no other visible code of the domain and kind (both kinds with `verifyAcrossKinds`, which the client
uses: the handbook compares codes only). A non-member of the other kind with a member's very code (vanilla's ore
block and item) can't be excluded by any pattern: it is tolerated and reported as `groupby-shared-code`. With no
exact pattern `groupby-inexact` is reported and nothing is written. Codes whose stacks sit in different groups get
`groupby-conflict`. `Exclude` is set for codes whose every entry is hidden. Groups made only of one code's attribute
stacks need no pattern. `PatternKindByGroup` says how each pattern was built. Matcher semantics and examples:
[docs/variant-grouping/handbook.md](../../../docs/variant-grouping/handbook.md#patterns).

## Issues

`TidyResolution.Issues` and `HandbookPlan.Issues` (kind slugs): `property-missing`, `variant-mismatch`,
`property-empty` (a material worldproperties list with no codes), `placeholder-unresolved`,
`duplicate-group-id`, `representative-unmatched`, `rule-unused`,
`groupby-inexact`, `groupby-conflict`, `groupby-shared-code`.
