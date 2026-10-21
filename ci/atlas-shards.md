# Atlas shards

`ci/atlas-shards.json` is the Atlas job's matrix: one `{shard, filter}` per job, `filter` a
`dotnet test --filter` over the scenario classes' fully qualified names. It is the one list:
the `tree` job prints it (filtered, on a sliced PR run) as the `atlas` job's matrix
(`python3 ci/paths.py outputs`), and the planner reads it to find which shards a changed
scenario class runs in (docs/merge-queue.md, "Sliced PR runs"). JSON has no comments, so
why each shard is what it is lives here.

`tools/tests/test_ci_paths.py` holds the file to the classes: every test method of every
test class in `tests/PackTests` must be selected by exactly one shard. A new class that
no filter selects, or that two select, fails it, where it used to rest on `rest` being
written as the exact complement of the rest. Its filters may use only
`FullyQualifiedName` conditions (`~`, `!~`, `=`, `!=`), `&`, `|` and parentheses, which
is what the planner evaluates.

## Why the shards are what they are

Atlas runs one embedded server per process, so classes can only run in parallel as
separate jobs, and every scenario class pays its own server boot: 45-55 s on the default
world, 65-90 s on a seeded `standard` one, most of it asset loading (every locked mod),
which no world option shortens. So classes on the same world share one:
SharedWorldScenarios is every feature that needs only the plain world, WoodworkingScenarios
the woodworking chain, WoodworkingRosserScenarios the rosser and its debarked trunks,
TradingScenarios every trading feature on the plain world, PackFixScenarios the
pack-authored fix mods, each a partial class over one file per feature (a new feature adds
a file, not a class). The item and recipe classes are the exception: each builds the pack's
export (over a minute) and fills parts of it again to compare, so each has a shard to
itself. `fixes` is the fix mods' class and `tidy` is Tidy Variants', both matched by name,
so a new class of either kind joins its shard. `fixes` also runs ChutesNoBacktrackScenarios,
a plain world of its own kept out of `rest` (which sits at its time limit).

`woodworking`, `woodworking-rosser` and `woodworking-eidolon` (the eidolon's haul and crew
orders, WoodworkingEidolonScenarios) are the three woodworking classes, each a world of
machine runs (their long phases on the virtual clock, tests/PackTests/README.md), kept out
of `rest` and apart from each other; `woodworking-rosser` also runs
CrucibleFurnaceScenarios, and `woodworking-eidolon` MapRevealScenarios (a plain world with
deposit checks off in its ModConfig, so nothing but the reveal touches the columns it
watches) and SpringsWorldgenScenarios (a seeded standard world of its own); the terms are
anchored on `PackTests.` and end in `.` so that only that class name matches, not a test
method named after a feature or a class whose name extends another's.

The Ore* classes are five shards, each mostly server boots (a seeded standard world each):
`ore-cells` is ore cells (OreCellsScenarios, with stable veins' one-world order check run
last on its boot) and ore processing (the plain world with its switch on; its export
records are in RecipeExportScenarios, which builds the export anyway); `ore-anchor` and
`ore-far` are stable veins' two generation orders on seed 424242 (OreCellOrderAnchorFirst,
OreCellOrderFarFirst), and `ore-seam` the same for seed 101's seam (every OreCellOrderSeam*
class, matched by prefix): each must be a fresh world, so they cannot share a boot, and
they run on the virtual clock with no player joined; `ore` is the complement, ore maps
with the ore admin tools (OreMapsScenarios), so a new Ore* class lands there. Checking a
deposit generates its columns and the 11x11 around them, real generation the virtual clock
cannot shorten, so the classes that check many are split across worlds: the grade pockets
scenarios are a dozen deposits each, `gradepockets-cit` (GradePocketsCopperIronTinScenarios,
the cells holding the large deposits its variety check needs) and `gradepockets-zlb`
(GradePocketsZincLeadBismuthScenarios).

The trading shards split the Trading* classes: `trading-core` is the camp grid
(TradingCoreScenarios, a seeded standard world) with TradingScenarios; the trader maps are
three classes on worlds of their own: `trading-maps-shelf` (TradingMapsShelfScenarios, a
prospector's shelf and its surveys), `trading-maps-approach` (TradingMapsApproachScenarios,
nearing a far camp) and `trading-features` (TradingMapsScenarios, gravel maps, standing and
camp leads); `trading-misc` is every other Trading* class (story and admin, each a seeded
standard world), the complement, so a new seeded Trading class lands there, and a new
plain-world trading feature joins TradingScenarios.

`switches` and `clear-handcar` are four classes that cannot share the plain world, two to
a shard (four boots ran past the limit): SwitchesOffScenarios and ConfigDefaultsScenarios
(each its own ModConfig), ClearCommandScenarios (a survival world) and HandcarScenarios
(three players, and the shared world has no slots left).

The eidolon's Eidolon* classes (each its own plain world, a server boot apiece, but the
EidolonReal* ones) are thirteen shards. Their jobs run in game time on the virtual clock (tests/PackTests/README.md), a
minute of digging taking a few seconds, so a shard is mostly its boots: `eidolon` the
entity and the command tool, `eidolon-fittings` its fittings (EidolonFittedScenarios) and
docking to strip and refit them (EidolonDockScenarios), `eidolon-mine` mining alone, in
tunnels and in teams (EidolonMineScenarios, EidolonTunnelScenarios, every EidolonTeamMine*
class, matched by prefix), `eidolon-tall` the tall tunnels and deep pits
(EidolonTallTunnelScenarios) and sectors (EidolonSectorScenarios), `eidolon-ahead` crews of
two and three by look-ahead routes held to today's makespans (EidolonLookaheadScenarios,
with WideDensityProspectingScenarios, a world of its own kept out of `rest`),
`eidolon-team` waiting in a shared job (EidolonTeamWaitScenarios) and where it walks and
climbs (EidolonFootingScenarios), `eidolon-overhang` two mining a hillside whose edge wanders
across the area (EidolonOverhangScenarios, a long dig), `eidolon-fell` felling alone and together
(EidolonFellScenarios, EidolonTeamFellScenarios), `eidolon-orders` carrying and guarding
(EidolonCarryScenarios, EidolonGuardScenarios), `eidolon-hillside` two on a player's hillside
for 8 minutes of game time (EidolonHillsideScenarios: its export placed, then the longest
single run of mining in these shards), the three `eidolon-real-*` mining on ground from the
game's own world generation (each a seeded standard world, seed 31337, its spots found near
spawn: tests/PackTests/RealTerrain.cs): `eidolon-real-slopes` a hillside and two cuts into a
mountain (EidolonRealSlopeScenarios), `eidolon-real-ground` a pit and rough ground
(EidolonRealGroundScenarios), each small area run to its job's end, and `eidolon-real-large`
the owner's size of area on a hillside and on rough ground for 8 minutes of game time each
(EidolonRealLargeScenarios; matched by the `EidolonReal` prefix out of `eidolon-jobs`, so a new
EidolonReal* class needs a shard of its own), and `eidolon-jobs` every other Eidolon*
class (chunk loading, the mine window grown round a crew, EidolonMineWindowScenarios), the complement of the others, kept short so a new one can join it
(with VirtualClockScenarios, a world of its own kept out of `rest`).

A class's scenarios run one after another on its server (a boot is about a minute); keep
each shard's sum, boots included, near three and a half minutes, splitting a class or a
shard that grows. `trunks` is the trunk entity classes and the pickling tub, each joining
more players than the plain world has slots left for. `rest` is the complement filter, so
any other new class lands there without anyone having to list it; today it is
SharedWorldScenarios alone, so a new class needing its own world should get a shard (or
join one with room) rather than stay there. `rest` also builds and tests the local-only
icon export and ore survey mods and runs the rusty gear check (`if: matrix.shard ==
'rest'` steps in ci.yml), so the planner keeps `rest` in any sliced plan whose change
touches their inputs (`REST_STEP_INPUTS` in ci/paths.py).

Each shard's dotnet test run (compiling excluded: that is the step before it) must finish
in four and a half minutes or the shard fails (`Shard ran within its time limit`), so a
growing shard is split when it grows and not after it has crept up for months; aim for
three and a half.

## Feature slicing

A PR run that changes one of the pack's own mod's features runs only some shards
(docs/merge-queue.md, "Features"; the map is `ci/features.json`, read by `ci/features.py`). It
always runs five, `always` in the map, because every feature can break them:

- `items` and `recipes`: the exporter walks every feature's blocks, items and recipes (and its
  own records for each machine), and ItemExportScenarios and RecipeExportScenarios (every
  `RecipeExport*` file is a part of it) build the whole export and check it. A new item, a
  recipe or a switch's ownership changes what they compare.
- `switches`: SwitchesOffScenarios boots the mod with its switches off, each feature as the
  mod it changes ships, and ConfigDefaultsScenarios brings an old install's settings to this
  tree's defaults; a new setting, or a feature that misbehaves with its switch off, fails
  there and nowhere else.
- `tidy`: the Tidy Variants classes resolve their rules against every item in the pack and
  hold each to one group, and the tiles to a budget; a feature's new block or item variants
  can break either.
- `rest`: SharedWorldScenarios, whose parts are most features' plain-world scenarios and the
  pack as a whole: every locked mod loading cleanly at its locked version, the boot log and
  its format errors (PackLoadScenarios, ReadsBootLog), the mod's systems
  (SeraphHorizonsModScenarios), a fresh install's config defaults
  (ConfigDefaultsFreshScenarios) and the pack check.

So a machine whose scenarios are all SharedWorldScenarios and RecipeExportScenarios parts (the
draw bench, the press brake, ...) runs those five; the eidolon adds its nine, trading its five,
the ore code its seven. A new shard does not need the map: a feature lists classes, not shards,
and the filters pick the shards. A new class that exercises a feature without naming its code
(a woodworking class that the trunk entities move trunks in) is added to that feature's
`classes`.
